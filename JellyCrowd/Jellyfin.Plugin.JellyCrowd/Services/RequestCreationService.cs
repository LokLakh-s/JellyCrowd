using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IRequestCreationService"/>.
/// </summary>
public sealed class RequestCreationService : IRequestCreationService
{
  private readonly IRequestStore _store;
  private readonly IQuotaService _quotaService;
  private readonly INotificationService _notificationService;
  private readonly IDownloadDispatcher _downloadDispatcher;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly ITmdbClient _tmdbClient;
  private readonly IRequestCreationGate _creationGate;
  private readonly IContentRestrictionService _restrictions;
  private readonly Func<PluginConfiguration?> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="RequestCreationService"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="quotaService">The quota service used to enforce per-user limits.</param>
  /// <param name="notificationService">The notification service.</param>
  /// <param name="downloadDispatcher">The download dispatcher triggered on approval.</param>
  /// <param name="libraryMatcher">The library matcher (what is already on disk).</param>
  /// <param name="tmdbClient">The TMDB client (episode lists, air dates, genres for auto-approval).</param>
  /// <param name="creationGate">Serializes request creation per user, so duplicate checks cannot race.</param>
  /// <param name="restrictions">Refuses titles above the user's parental restriction.</param>
  /// <param name="config">The plugin configuration accessor; <c>null</c> while the plugin is not loaded.</param>
  public RequestCreationService(
    IRequestStore store,
    IQuotaService quotaService,
    INotificationService notificationService,
    IDownloadDispatcher downloadDispatcher,
    ILibraryMatcher libraryMatcher,
    ITmdbClient tmdbClient,
    IRequestCreationGate creationGate,
    IContentRestrictionService restrictions,
    Func<PluginConfiguration?> config)
  {
    _store = store;
    _quotaService = quotaService;
    _notificationService = notificationService;
    _downloadDispatcher = downloadDispatcher;
    _libraryMatcher = libraryMatcher;
    _tmdbClient = tmdbClient;
    _creationGate = creationGate;
    _restrictions = restrictions;
    _config = config;
  }

  /// <inheritdoc />
  public Task<RequestCreationResult> CreateAsync(Guid userId, CreateRequestDto dto, CancellationToken cancellationToken)
  {
    // A child account requests nothing itself: their parents do, for them.
    if (_config() is { } config && ChildAccountPolicy.IsChild(config, userId))
    {
      return Task.FromResult(RequestCreationResult.Refused(RequestCreationOutcome.Forbidden, "Child accounts don't request: a parent requests for them."));
    }

    return CreateCoreAsync(userId, dto, forChild: false, cancellationToken);
  }

  /// <inheritdoc />
  public Task<RequestCreationResult> CreateForChildAsync(Guid parentId, Guid childId, CreateRequestDto dto, CancellationToken cancellationToken)
  {
    if (_config() is not { } config || !ChildAccountPolicy.IsParentOf(config, parentId, childId))
    {
      return Task.FromResult(RequestCreationResult.Refused(RequestCreationOutcome.Forbidden, "You can only request for your own children."));
    }

    return CreateCoreAsync(childId, dto, forChild: true, cancellationToken);
  }

  // The one path every request takes. A request a parent makes for their child skips only the "may this
  // user request" switch, which is about the child acting on their own; everything else — the child's age,
  // quota, limits and approval — applies as for any request of theirs.
  private async Task<RequestCreationResult> CreateCoreAsync(Guid userId, CreateRequestDto dto, bool forChild, CancellationToken cancellationToken)
  {
    if (dto is null || string.IsNullOrWhiteSpace(dto.Title))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.Invalid, "A title is required.");
    }

    if (!IsValidMediaType(dto.MediaType))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.Invalid, "The 'mediaType' must be 'movie' or 'tv'.");
    }

    var config = _config();

    // Access control: an admin can disable requests for a given user.
    if (!forChild && config is not null && !RequestPolicy.CanRequest(config, userId))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.Forbidden, "Requests are disabled for your account.");
    }

    // Instance scope: movies and/or TV can be turned off, and TV can be limited to certain granularities.
    if (config is not null && !RequestPolicy.IsMediaTypeEnabled(config, dto.MediaType))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.Forbidden, "This media type is not available on this server.");
    }

    if (config is not null && !RequestPolicy.IsRequestGranularityAllowed(config, dto.MediaType, dto.Season, dto.Episode))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.Forbidden, "This request granularity is not allowed on this server.");
    }

    if (await CheckParentalRestrictionAsync(userId, dto.MediaType, dto.TmdbId, cancellationToken).ConfigureAwait(false) is { } refused)
    {
      return refused;
    }

    // One request at a time per user: what they already asked for is checked and the new request recorded
    // as a single step, so two requests sent in the same instant cannot both slip past the check.
    using var creation = await _creationGate.EnterAsync(userId, cancellationToken).ConfigureAwait(false);

    // Weighed against what the user already has — on disk and owned, or on its way — episode by episode:
    // owning a season no longer blocks completing the series, and a fulfilled season missing an episode can
    // be completed, while anything already covered is still refused.
    var coverage = await EvaluateCreationAsync(userId, dto.MediaType, dto.TmdbId, dto.Season, dto.Episode, cancellationToken).ConfigureAwait(false);
    if (coverage.AlreadyCovered)
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.AlreadyCovered, "You already have this title, or it is already on its way.");
    }

    // Larger than the whole quota, it could never be downloaded: refuse it now rather than hold it forever
    // waiting for space that can never exist.
    if (ExceedsWholeQuota(userId, dto.MediaType, coverage.EpisodesToReserve))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.TooLarge, "This request is larger than your whole disk quota.");
    }

    if (config is not null)
    {
      var cap = RequestPolicy.MaxRequestsPerPeriod(config, userId);
      if (cap > 0)
      {
        var since = DateTime.UtcNow - PeriodToSpan(config.RequestPeriod);
        var recent = await _store.CountUserRequestsSinceAsync(userId, since, cancellationToken).ConfigureAwait(false);
        if (recent >= cap)
        {
          return RequestCreationResult.Refused(RequestCreationOutcome.RateLimited, "You have reached your request limit for this period.");
        }
      }
    }

    // Approval mode: requests stay pending when admin approval is required, unless the user is trusted
    // or the request matches the auto-approval size (and optional genre) rule. Even auto-approved
    // requests are held as pending (not rejected) when they would exceed the disk quota, for the admin
    // to arbitrate. Genres are resolved from TMDB only when a genre all-list is configured.
    var autoApprove = false;
    if (config is not null)
    {
      IReadOnlyList<string> genres = Array.Empty<string>();
      if (config.AutoApproveGenres.Count > 0 && !RequestPolicy.IsTrusted(config, userId))
      {
        var details = await _tmdbClient.GetDetailsAsync(dto.MediaType, dto.TmdbId, "en-US", cancellationToken).ConfigureAwait(false);
        genres = details?.Genres ?? Array.Empty<string>();
      }

      autoApprove = RequestPolicy.ShouldAutoApprove(config, userId, dto.MediaType, genres);
    }

    var requireApproval = (config?.RequireApproval ?? true) && !autoApprove;

    // A not-yet-released title reserves no quota yet — it can't download until it is out, so it is allowed
    // now regardless of quota and re-checked when it becomes due (see the download dispatcher). Only a
    // title that is downloadable now is gated against the quota here.
    var now = DateTime.UtcNow;
    var releaseDate = await ResolveReleaseDateAsync(dto.MediaType, dto.TmdbId, dto.Season, dto.Episode, dto.ReleaseDate, cancellationToken).ConfigureAwait(false);
    var (desiredAt, awaitingReleaseDate) = await ScheduleAsync(dto.MediaType, dto.TmdbId, releaseDate, dto.DesiredAt, now, cancellationToken).ConfigureAwait(false);
    var downloadableNow = desiredAt <= now;
    var episodes = coverage.EpisodesToReserve;
    var withinQuota = !downloadableNow || await _quotaService.CanRequestAsync(userId, dto.MediaType, episodes, cancellationToken).ConfigureAwait(false);
    var status = (requireApproval || !withinQuota) ? RequestStatus.Pending : RequestStatus.Approved;

    // Held purely by the quota (it did not need an admin): flag it so it resumes automatically — i.e. is
    // promoted to Approved without an admin decision — once the user's quota frees up.
    var heldForQuota = !withinQuota && !requireApproval;

    var created = await _store.CreateAsync(
      new RequestRecord
      {
        UserId = userId,
        TmdbId = dto.TmdbId,
        MediaType = dto.MediaType,
        Title = dto.Title,
        PosterPath = dto.PosterPath,
        ReleaseDate = releaseDate,
        Season = dto.Season,
        Episode = dto.Episode,
        EstimatedEpisodes = episodes,
        DesiredAt = desiredAt,
        AwaitingReleaseDate = awaitingReleaseDate,
        Status = status,
        HeldForQuota = heldForQuota
      },
      cancellationToken).ConfigureAwait(false);

    _ = _notificationService.NotifyRequestEventAsync(created, NotificationEvent.Created, CancellationToken.None);

    // Warn the requester when their request is held purely because they are at their disk quota
    // (not the normal "awaiting admin approval" case), so they understand why it is not progressing.
    if (heldForQuota)
    {
      NotifyHeldForQuota(created);
    }

    // Auto-approved requests are dispatched right away (no-op if not yet due / no backend configured).
    if (status == RequestStatus.Approved)
    {
      _ = _downloadDispatcher.DispatchAsync(created, CancellationToken.None);
    }

    return RequestCreationResult.Created(created);
  }

  /// <inheritdoc />
  public async Task<RequestCreationResult> CreateOnBehalfAsync(AdminCreateRequestDto dto, CancellationToken cancellationToken)
  {
    if (dto is null || string.IsNullOrWhiteSpace(dto.Title))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.Invalid, "A title is required.");
    }

    if (!IsValidMediaType(dto.MediaType))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.Invalid, "The 'mediaType' must be 'movie' or 'tv'.");
    }

    if (dto.UserId == Guid.Empty)
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.Invalid, "A target user is required.");
    }

    // Respect the instance scope even for on-behalf requests (movies/TV enabled + TV granularity).
    if (_config() is { } forUserConfig)
    {
      if (!RequestPolicy.IsMediaTypeEnabled(forUserConfig, dto.MediaType))
      {
        return RequestCreationResult.Refused(RequestCreationOutcome.Forbidden, "This media type is not available on this server.");
      }

      if (!RequestPolicy.IsRequestGranularityAllowed(forUserConfig, dto.MediaType, dto.Season, dto.Episode))
      {
        return RequestCreationResult.Refused(RequestCreationOutcome.Forbidden, "This request granularity is not allowed on this server.");
      }
    }

    using var creation = await _creationGate.EnterAsync(dto.UserId, cancellationToken).ConfigureAwait(false);
    var coverage = await EvaluateCreationAsync(dto.UserId, dto.MediaType, dto.TmdbId, dto.Season, dto.Episode, cancellationToken).ConfigureAwait(false);
    if (coverage.AlreadyCovered)
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.AlreadyCovered, "This user already has this title, or it is already on its way.");
    }

    if (ExceedsWholeQuota(dto.UserId, dto.MediaType, coverage.EpisodesToReserve))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.TooLarge, "This request is larger than the user's whole disk quota.");
    }

    // An approved request that does not fit the user's quota yet waits for space, exactly like their own.
    var now = DateTime.UtcNow;
    var releaseDate = await ResolveReleaseDateAsync(dto.MediaType, dto.TmdbId, dto.Season, dto.Episode, dto.ReleaseDate, cancellationToken).ConfigureAwait(false);
    var (desiredAt, awaitingReleaseDate) = await ScheduleAsync(dto.MediaType, dto.TmdbId, releaseDate, null, now, cancellationToken).ConfigureAwait(false);
    var status = dto.Status ?? RequestStatus.Approved;
    var heldForQuota = status == RequestStatus.Approved
      && desiredAt <= now
      && !await _quotaService.CanRequestAsync(dto.UserId, dto.MediaType, coverage.EpisodesToReserve, cancellationToken).ConfigureAwait(false);
    if (heldForQuota)
    {
      status = RequestStatus.Pending;
    }

    var created = await _store.CreateAsync(
      new RequestRecord
      {
        UserId = dto.UserId,
        TmdbId = dto.TmdbId,
        MediaType = dto.MediaType,
        Title = dto.Title,
        PosterPath = dto.PosterPath,
        ReleaseDate = releaseDate,
        Season = dto.Season,
        Episode = dto.Episode,
        EstimatedEpisodes = coverage.EpisodesToReserve,
        DesiredAt = desiredAt,
        AwaitingReleaseDate = awaitingReleaseDate,
        Status = status,
        HeldForQuota = heldForQuota
      },
      cancellationToken).ConfigureAwait(false);

    _ = _notificationService.NotifyRequestEventAsync(created, NotificationEvent.Created, CancellationToken.None);
    if (heldForQuota)
    {
      NotifyHeldForQuota(created);
    }

    if (status == RequestStatus.Approved)
    {
      _ = _downloadDispatcher.DispatchAsync(created, CancellationToken.None);
    }

    return RequestCreationResult.Created(created);
  }

  private static bool IsValidMediaType(string mediaType)
    => string.Equals(mediaType, "movie", StringComparison.Ordinal)
       || string.Equals(mediaType, "tv", StringComparison.Ordinal);

  private static TimeSpan PeriodToSpan(RequestPeriod period) => period switch
  {
    RequestPeriod.Day => TimeSpan.FromDays(1),
    RequestPeriod.Month => TimeSpan.FromDays(30),
    _ => TimeSpan.FromDays(7)
  };

  private static bool IsInFlight(RequestRecord request) => request.Status is RequestStatus.Pending or RequestStatus.Approved;

  private static bool IsOwned(RequestRecord request) => request.Status == RequestStatus.Available;

  // The wanted episodes already covered by one of the user's TV requests for this title in the given state.
  private static HashSet<EpisodeKey> CoveredBy(List<RequestRecord> requests, Func<RequestRecord, bool> inState, IReadOnlyCollection<EpisodeKey> wanted, int tmdbId)
  {
    var covered = new HashSet<EpisodeKey>();
    var scopes = requests
      .Where(r => inState(r) && r.TmdbId == tmdbId && string.Equals(r.MediaType, "tv", StringComparison.Ordinal))
      .Select(RequestScope.Of)
      .ToList();
    if (scopes.Count == 0)
    {
      return covered;
    }

    foreach (var key in wanted)
    {
      var episodeScope = new RequestScope("tv", tmdbId, key.Season, key.Episode);
      if (scopes.Any(scope => scope.Contains(episodeScope)))
      {
        covered.Add(key);
      }
    }

    return covered;
  }

  // Refuses a title above the user's parental restriction (Jellyfin parental control or child group).
  // The catalog already hides such titles; this is what stops a hand-made call. A rating that cannot be
  // looked up is a refusal too — a restricted user is never let through on a guess.
  private async Task<RequestCreationResult?> CheckParentalRestrictionAsync(Guid userId, string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    var restriction = _restrictions.For(userId);
    if (!restriction.IsRestricted)
    {
      return null;
    }

    try
    {
      return await _restrictions.IsAllowedAsync(restriction, mediaType, tmdbId, cancellationToken).ConfigureAwait(false)
        ? null
        : RequestCreationResult.Refused(RequestCreationOutcome.Forbidden, "This title is above your parental rating limit.");
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
      || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
    {
      return RequestCreationResult.Refused(RequestCreationOutcome.Unavailable, "The title's rating cannot be checked right now.");
    }
  }

  /// <summary>
  /// Works out what a prospective request adds for a user: whether it is already covered — owned and on
  /// disk, or on its way — and how many episodes it would have to download. A movie or a single episode is
  /// one item. A season or a series is weighed episode by episode against TMDB's episode list and the
  /// library, so completing a partly owned show is allowed and reserves only what is missing.
  /// </summary>
  /// <param name="userId">The user the request is for.</param>
  /// <param name="mediaType">The requested media type.</param>
  /// <param name="tmdbId">The TMDB id of the title.</param>
  /// <param name="season">The requested season, or <c>null</c> for a whole series.</param>
  /// <param name="episode">The requested episode, or <c>null</c> for a whole season/series.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>Whether the request is already covered, and the episodes it must reserve.</returns>
  private async Task<CoverageDecision> EvaluateCreationAsync(Guid userId, string mediaType, int tmdbId, int? season, int? episode, CancellationToken cancellationToken)
  {
    var wantedScope = new RequestScope(mediaType, tmdbId, season, episode);
    var active = (await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false))
      .Where(r => r.Status != RequestStatus.Denied)
      .ToList();

    // A movie: covered by any of the user's requests for it, as before.
    if (!string.Equals(mediaType, "tv", StringComparison.Ordinal))
    {
      return new CoverageDecision(active.Any(r => RequestScope.Of(r).Contains(wantedScope)), 1);
    }

    // One episode: a single item, whose presence reads straight off the library. It reserves one estimate.
    if (season is int s && episode is int e)
    {
      var wanted = new[] { new EpisodeKey(s, e) };
      var present = _libraryMatcher.FindEpisodeItemId(tmdbId, s, e) is null ? Array.Empty<EpisodeKey>() : wanted;
      var decision = RequestCoverage.Evaluate(wanted, present, CoveredBy(active, IsInFlight, wanted, tmdbId), CoveredBy(active, IsOwned, wanted, tmdbId));
      return decision with { EpisodesToReserve = 1 };
    }

    var episodes = await ListWantedEpisodesAsync(tmdbId, season, cancellationToken).ConfigureAwait(false);
    if (episodes is null)
    {
      // TMDB cannot list the episodes: compare request scopes instead. Anything already covered is refused;
      // a broader request is allowed, reserving the full episode count since nothing can be subtracted.
      if (active.Any(r => RequestScope.Of(r).Contains(wantedScope)))
      {
        return new CoverageDecision(true, 0);
      }

      IReadOnlyList<Season>? seasons = null;
      try
      {
        seasons = await _tmdbClient.GetSeasonsAsync(tmdbId, "en-US", cancellationToken).ConfigureAwait(false);
      }
      catch (Exception)
      {
        // Unknown counts reserve one episode, as RequestFootprint documents.
      }

      return new CoverageDecision(false, RequestFootprint.EpisodesCovered(mediaType, season, null, seasons));
    }

    var inLibrary = _libraryMatcher.ListEpisodeKeys(tmdbId, season) ?? Array.Empty<EpisodeKey>();
    return RequestCoverage.Evaluate(episodes, inLibrary, CoveredBy(active, IsInFlight, episodes, tmdbId), CoveredBy(active, IsOwned, episodes, tmdbId));
  }

  // Every episode a season or series request covers, according to TMDB. A whole series leaves out specials,
  // which are not fetched with it. Null when TMDB cannot list them.
  private async Task<IReadOnlyCollection<EpisodeKey>?> ListWantedEpisodesAsync(int tmdbId, int? season, CancellationToken cancellationToken)
  {
    try
    {
      var seasonNumbers = new List<int>();
      if (season is int only)
      {
        seasonNumbers.Add(only);
      }
      else
      {
        foreach (var listed in await _tmdbClient.GetSeasonsAsync(tmdbId, "en-US", cancellationToken).ConfigureAwait(false))
        {
          if (listed.SeasonNumber > 0)
          {
            seasonNumbers.Add(listed.SeasonNumber);
          }
        }
      }

      var keys = new HashSet<EpisodeKey>();
      foreach (var number in seasonNumbers)
      {
        foreach (var listed in await _tmdbClient.GetSeasonEpisodesAsync(tmdbId, number, "en-US", cancellationToken).ConfigureAwait(false))
        {
          keys.Add(new EpisodeKey(number, listed.EpisodeNumber));
        }
      }

      return keys.Count == 0 ? null : keys;
    }
    catch (Exception)
    {
      return null;
    }
  }

  // When a request is due, and whether its title has no release date yet. A movie is scheduled on its home
  // release (digital or physical, see MovieAvailability), not on its cinema date: Radarr takes nothing before
  // it. Without TMDB, or for a show, the release date decides as before.
  private async Task<(DateTime DesiredAt, bool AwaitingReleaseDate)> ScheduleAsync(string mediaType, int tmdbId, string? releaseDate, DateTime? requestedDesiredAt, DateTime now, CancellationToken cancellationToken)
  {
    var desiredAt = RequestScheduling.ResolveDesiredAt(releaseDate, requestedDesiredAt, now);
    if (!string.Equals(mediaType, "movie", StringComparison.Ordinal))
    {
      return (desiredAt, false);
    }

    try
    {
      var release = await _tmdbClient.GetMovieReleaseAsync(tmdbId, cancellationToken).ConfigureAwait(false);
      if (MovieAvailability.HomeRelease(release) is { } home)
      {
        desiredAt = RequestScheduling.ResolveDesiredAt(home.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), requestedDesiredAt, now);
      }

      return (desiredAt, MovieAvailability.IsUnannounced(release));
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      return (desiredAt, false);
    }
  }

  // The release date a request is scheduled on. An episode takes its own air date from TMDB rather than the
  // one the client sent: while TMDB has not published it, the client falls back to the series' first air
  // date, which scheduled every episode of a new season on its premiere. An air date TMDB does not know yet
  // stays unknown, and the air-date refresher fills it in once it is published.
  private async Task<string?> ResolveReleaseDateAsync(string mediaType, int tmdbId, int? season, int? episode, string? sentReleaseDate, CancellationToken cancellationToken)
  {
    if (!string.Equals(mediaType, "tv", StringComparison.Ordinal) || season is not int seasonNumber || episode is not int episodeNumber)
    {
      return sentReleaseDate;
    }

    try
    {
      var episodes = await _tmdbClient.GetSeasonEpisodesAsync(tmdbId, seasonNumber, "en-US", cancellationToken).ConfigureAwait(false);
      var listed = episodes.FirstOrDefault(e => e.EpisodeNumber == episodeNumber);
      return listed is null ? sentReleaseDate : listed.AirDate;
    }
    catch (Exception)
    {
      return sentReleaseDate;
    }
  }

  // Whether a request would reserve more than the user's whole quota, so that it could never be downloaded.
  private bool ExceedsWholeQuota(Guid userId, string mediaType, int episodes)
  {
    var quota = _quotaService.GetQuotaBytes(userId);
    return quota > 0 && _quotaService.ReservationBytes(new RequestRecord { MediaType = mediaType, EstimatedEpisodes = episodes }) > quota;
  }

  // Tells a requester their request is waiting for disk space (not for an administrator), so they
  // understand why it is not progressing.
  private void NotifyHeldForQuota(RequestRecord created)
  {
    var heldStrings = ServerStrings.ForMember(_config()?.Language, created.UserId);
    _ = _notificationService.NotifyPersonalAsync(
      created.UserId,
      PersonalNotifyKind.QuotaExpiry,
      created.Title,
      heldStrings("notif_quota_held_subject"),
      heldStrings("notif_quota_held_request_body").Replace("{title}", created.Title, StringComparison.Ordinal),
      created.PosterPath,
      CancellationToken.None);
  }
}
