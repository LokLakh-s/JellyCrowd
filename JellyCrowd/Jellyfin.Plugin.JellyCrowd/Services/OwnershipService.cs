using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IOwnershipService"/>, over the request store and the library.
/// </summary>
public sealed class OwnershipService : IOwnershipService
{
  private readonly IRequestStore _store;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly IRequestCreationGate _creationGate;
  private readonly INotificationService _notificationService;
  private readonly IActivityLog _activityLog;
  private readonly ITmdbClient _tmdb;
  private readonly ILogger<OwnershipService> _logger;
  private readonly Func<Guid, string> _resolveUserName;

  /// <summary>
  /// Initializes a new instance of the <see cref="OwnershipService"/> class.
  /// </summary>
  /// <param name="store">The request store (ownerships are available requests).</param>
  /// <param name="libraryMatcher">The library matcher.</param>
  /// <param name="creationGate">Serializes each member's request changes.</param>
  /// <param name="notificationService">Tells members what they were given.</param>
  /// <param name="activityLog">The activity log.</param>
  /// <param name="tmdb">The TMDB client (the poster of a media nobody requested).</param>
  /// <param name="logger">The logger.</param>
  /// <param name="resolveUserName">Resolves a member's name.</param>
  public OwnershipService(
    IRequestStore store,
    ILibraryMatcher libraryMatcher,
    IRequestCreationGate creationGate,
    INotificationService notificationService,
    IActivityLog activityLog,
    ITmdbClient tmdb,
    ILogger<OwnershipService> logger,
    Func<Guid, string> resolveUserName)
  {
    _store = store;
    _libraryMatcher = libraryMatcher;
    _creationGate = creationGate;
    _notificationService = notificationService;
    _activityLog = activityLog;
    _tmdb = tmdb;
    _logger = logger;
    _resolveUserName = resolveUserName;
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<OwnedMediaDto>> ListAsync(CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var owned = all.Where(r => r.Status == RequestStatus.Available).ToList();

    // A request remembers the title as TMDB names it (the library may name it after another series) and its poster.
    var known = all
      .GroupBy(r => (r.MediaType, r.TmdbId))
      .ToDictionary(g => g.Key, g => (Title: g.First().Title, Poster: g.Select(r => r.PosterPath).FirstOrDefault(p => !string.IsNullOrEmpty(p))));

    var result = new List<OwnedMediaDto>();
    foreach (var item in _libraryMatcher.ListLibraryMedia())
    {
      known.TryGetValue((item.MediaType, item.TmdbId), out var request);
      var dto = new OwnedMediaDto
      {
        JellyfinItemId = item.JellyfinItemId,
        MediaType = item.MediaType,
        TmdbId = item.TmdbId,
        Title = string.IsNullOrWhiteSpace(request.Title) ? item.Title : request.Title,
        PosterPath = request.Poster,
        Season = item.Season,
        SizeBytes = item.SizeBytes
      };

      var holdings = owned
        .Where(r => r.TmdbId == item.TmdbId
          && string.Equals(r.MediaType, item.MediaType, StringComparison.Ordinal)
          && MediaScope.Overlaps(item.Season, null, r.Season, r.Episode))
        .GroupBy(r => r.UserId);
      foreach (var holding in holdings)
      {
        var holder = new MediaHolderDto
        {
          UserId = holding.Key,
          Name = _resolveUserName(holding.Key),
          SinceUtc = holding.Max(r => r.AvailableAt),
          WholeShow = item.Season is not null && holding.Any(r => r.Season is null),
          Leaving = holding.All(r => r.DeletionRequestedAt is not null)
        };

        // Only some episodes of the season: say which.
        if (!holder.WholeShow && holding.All(r => r.Episode is not null))
        {
          foreach (var episode in holding.Select(r => r.Episode!.Value).Distinct().Order())
          {
            holder.Episodes.Add(episode);
          }
        }

        dto.Owners.Add(holder);
      }

      var ordered = dto.Owners.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
      dto.Owners.Clear();
      ordered.ForEach(dto.Owners.Add);
      result.Add(dto);
    }

    return result
      .OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
      .ThenBy(m => m.Season ?? 0)
      .ToList();
  }

  /// <inheritdoc />
  public async Task<OwnershipGrant?> GiveOneAsync(Guid userId, OwnershipMediaRef media, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(media);

    // Resolve the exact library item for the scope (a season, an episode, or the whole movie/series), so
    // ownership points at what actually exists — giving is only for media that is already present.
    var itemId = FindOwnableItemId(_libraryMatcher, media.MediaType, media.TmdbId, media.Season, media.Episode);
    if (string.IsNullOrEmpty(itemId))
    {
      return null;
    }

    // Already owned by this user (this scope, or a broader one covering it) → renew rather than duplicate.
    using var creation = await _creationGate.EnterAsync(userId, cancellationToken).ConfigureAwait(false);
    var theirs = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    var owned = theirs.FirstOrDefault(r =>
      r.TmdbId == media.TmdbId
      && string.Equals(r.MediaType, media.MediaType, StringComparison.Ordinal)
      && r.Status == RequestStatus.Available
      && MediaScope.Overlaps(media.Season, media.Episode, r.Season, r.Episode));
    if (owned is not null)
    {
      var renewed = await _store.RenewAvailableAsync(owned.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
      return new OwnershipGrant(renewed ?? owned, Created: false);
    }

    var created = await _store.CreateAsync(
      new RequestRecord
      {
        UserId = userId,
        TmdbId = media.TmdbId,
        MediaType = media.MediaType,
        Title = media.Title,
        PosterPath = string.IsNullOrEmpty(media.PosterPath) ? await PosterOfAsync(media, cancellationToken).ConfigureAwait(false) : media.PosterPath,
        ReleaseDate = media.ReleaseDate,
        Season = media.Season,
        Episode = media.Episode,
        Status = RequestStatus.Available,
        JellyfinItemId = itemId,
        AvailableAt = DateTime.UtcNow
      },
      cancellationToken).ConfigureAwait(false);

    var t = ServerStrings.ForMember(Plugin.Instance?.Configuration?.Language, userId);
    var title = NotificationMessages.TitleOf(created, t);
    _ = _notificationService.NotifyPersonalAsync(
      userId,
      PersonalNotifyKind.None,
      created.Title,
      t("notif_assigned_subject"),
      t("notif_assigned_body").Replace("{title}", title, StringComparison.Ordinal),
      created.PosterPath,
      CancellationToken.None);
    return new OwnershipGrant(created, Created: true);
  }

  /// <inheritdoc />
  public async Task<OwnershipChangeResult> GiveAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<OwnershipMediaRef> media, string adminName, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(userIds);
    ArgumentNullException.ThrowIfNull(media);
    var result = new OwnershipChangeResult();
    foreach (var item in media)
    {
      if (!IsValid(item))
      {
        result.Failed++;
        continue;
      }

      var given = false;
      foreach (var userId in userIds.Where(u => u != Guid.Empty).Distinct())
      {
        var grant = await GiveOneAsync(userId, item, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
          break; // not in the library: the same for every member.
        }

        given = true;
        if (grant.Created)
        {
          result.Given++;
        }
        else
        {
          result.AlreadyOwned++;
        }
      }

      if (!given && userIds.Any(u => u != Guid.Empty))
      {
        result.Failed++;
      }
    }

    Log("gave", adminName, userIds, media, result.Given);
    return result;
  }

  /// <inheritdoc />
  public async Task<OwnershipChangeResult> RemoveAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<OwnershipMediaRef> media, string adminName, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(userIds);
    ArgumentNullException.ThrowIfNull(media);
    var result = new OwnershipChangeResult();
    IReadOnlyList<LibraryMediaItem>? library = null;
    foreach (var userId in userIds.Where(u => u != Guid.Empty).Distinct())
    {
      using var creation = await _creationGate.EnterAsync(userId, cancellationToken).ConfigureAwait(false);
      foreach (var item in media.Where(IsValid))
      {
        var theirs = (await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false))
          .Where(r => r.Status == RequestStatus.Available
            && r.TmdbId == item.TmdbId
            && string.Equals(r.MediaType, item.MediaType, StringComparison.Ordinal)
            && MediaScope.Overlaps(item.Season, item.Episode, r.Season, r.Episode))
          .ToList();
        if (theirs.Count == 0)
        {
          result.NotOwned++;
          continue;
        }

        foreach (var record in theirs)
        {
          // The whole show owned, one season taken: the member keeps the show's other seasons in the library.
          if (record.Season is null && item.Season is int taken && string.Equals(item.MediaType, "tv", StringComparison.Ordinal))
          {
            library ??= _libraryMatcher.ListLibraryMedia();
            var held = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
            await _store.ReplaceAsync(record.Id, OtherSeasons(record, taken, library, held), cancellationToken).ConfigureAwait(false);
          }
          else
          {
            await _store.DeleteAsync(record.Id, cancellationToken).ConfigureAwait(false);
          }
        }

        result.Removed++;
      }
    }

    Log("took", adminName, userIds, media, result.Removed);
    return result;
  }

  /// <summary>
  /// The ownerships a whole show's is cut into when one of its seasons is taken: one per other season in the
  /// library that the member does not already own on its own. They carry the whole show's dates (a pending
  /// deletion included), so their expiry does not restart and they do not count as new requests.
  /// </summary>
  /// <param name="wholeShow">The ownership of the whole show.</param>
  /// <param name="taken">The season taken.</param>
  /// <param name="library">The library's media.</param>
  /// <param name="held">The member's requests.</param>
  /// <returns>The ownerships to keep.</returns>
  internal static List<RequestRecord> OtherSeasons(RequestRecord wholeShow, int taken, IReadOnlyList<LibraryMediaItem> library, IReadOnlyList<RequestRecord> held)
  {
    ArgumentNullException.ThrowIfNull(wholeShow);
    ArgumentNullException.ThrowIfNull(library);
    ArgumentNullException.ThrowIfNull(held);
    return library
      .Where(m => m.TmdbId == wholeShow.TmdbId
        && string.Equals(m.MediaType, "tv", StringComparison.Ordinal)
        && m.Season is int s
        && s != taken
        && !held.Any(r => r.Id != wholeShow.Id
          && r.Status == RequestStatus.Available
          && r.TmdbId == wholeShow.TmdbId
          && string.Equals(r.MediaType, "tv", StringComparison.Ordinal)
          && r.Season == s
          && r.Episode is null))
      .Select(m => m.Season!.Value)
      .Distinct()
      .Order()
      .Select(season => new RequestRecord
      {
        UserId = wholeShow.UserId,
        TmdbId = wholeShow.TmdbId,
        MediaType = wholeShow.MediaType,
        Title = wholeShow.Title,
        PosterPath = wholeShow.PosterPath,
        ReleaseDate = wholeShow.ReleaseDate,
        Season = season,
        Status = RequestStatus.Available,
        RequestedAt = wholeShow.RequestedAt,
        DecidedAt = wholeShow.DecidedAt,
        DecidedBy = wholeShow.DecidedBy,
        JellyfinItemId = library.First(m => m.TmdbId == wholeShow.TmdbId && string.Equals(m.MediaType, "tv", StringComparison.Ordinal) && m.Season == season).JellyfinItemId,
        AvailableAt = wholeShow.AvailableAt,
        DeletionRequestedAt = wholeShow.DeletionRequestedAt,
        DispatchedAt = wholeShow.DispatchedAt
      })
      .ToList();
  }

  // A media given from the library may never have been requested, so nothing remembers its poster: take it
  // from TMDB, so the member's library and notification show it like any other. Best-effort.
  private async Task<string?> PosterOfAsync(OwnershipMediaRef media, CancellationToken cancellationToken)
  {
    try
    {
      return (await _tmdb.GetDetailsAsync(media.MediaType, media.TmdbId, "en-US", cancellationToken).ConfigureAwait(false))?.PosterPath;
    }
#pragma warning disable CA1031 // A missing poster must not stop the ownership.
    catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not fetch the TMDB poster of {MediaType} {TmdbId}.", media.MediaType, media.TmdbId);
      return null;
    }
  }

  private static bool IsValid(OwnershipMediaRef media)
    => media is not null
      && media.TmdbId > 0
      && (string.Equals(media.MediaType, "movie", StringComparison.Ordinal) || string.Equals(media.MediaType, "tv", StringComparison.Ordinal))
      && !string.IsNullOrWhiteSpace(media.Title);

  /// <summary>
  /// The library item an ownership points at: an episode, a season, or the whole movie/series. A season must
  /// hold an episode — Jellyfin keeps the season folder after its files are deleted, and that empty shell is
  /// not something to own (<see cref="ILibraryMatcher.FindItemId"/> applies the same rule to a series).
  /// </summary>
  /// <param name="matcher">The library matcher.</param>
  /// <param name="mediaType">The media type (movie or tv).</param>
  /// <param name="tmdbId">The TMDB id.</param>
  /// <param name="season">The season, or <c>null</c>.</param>
  /// <param name="episode">The episode, or <c>null</c>.</param>
  /// <returns>The Jellyfin item id, or <c>null</c> when it is not in the library.</returns>
  internal static string? FindOwnableItemId(ILibraryMatcher matcher, string mediaType, int tmdbId, int? season, int? episode)
  {
    ArgumentNullException.ThrowIfNull(matcher);
    if (!string.Equals(mediaType, "tv", StringComparison.Ordinal) || season is not int seasonNumber)
    {
      return matcher.FindItemId(mediaType, tmdbId);
    }

    if (episode is int episodeNumber)
    {
      return matcher.FindEpisodeItemId(tmdbId, seasonNumber, episodeNumber);
    }

    return matcher.FindEpisodeItemId(tmdbId, seasonNumber, null) is null
      ? null
      : matcher.FindSeasonItemId(tmdbId, seasonNumber);
  }

  // One line per change, whatever its size: a bulk change must not flood the (capped) activity log.
  private void Log(string verb, string adminName, IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<OwnershipMediaRef> media, int count)
  {
    if (count == 0)
    {
      return;
    }

    var titles = string.Join(", ", media.Select(m => m.Season is int s ? m.Title + " S" + s.ToString(CultureInfo.InvariantCulture) : m.Title).Distinct().Take(5))
      + (media.Count > 5 ? ", …" : string.Empty);
    var users = string.Join(", ", userIds.Distinct().Select(_resolveUserName));
    _ = _activityLog.LogAsync("info", "user", adminName + " " + verb + " " + titles + (verb == "gave" ? " to " : " from ") + users, adminName, CancellationToken.None);
  }
}
