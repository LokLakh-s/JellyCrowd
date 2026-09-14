using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IRequestReconciler"/>: marks approved requests available (and notifies) once
/// the matching title is found in the Jellyfin library, recording the library item id.
/// </summary>
public sealed class RequestReconciler : IRequestReconciler
{
  private readonly IRequestStore _store;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly INotificationService _notificationService;
  private readonly IDownloadDispatcher _dispatcher;
  private readonly ITmdbClient _tmdbClient;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<RequestReconciler> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="RequestReconciler"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="libraryMatcher">The library matcher.</param>
  /// <param name="notificationService">The notification service.</param>
  /// <param name="dispatcher">The download dispatcher (to nudge the backend to import a manual file).</param>
  /// <param name="tmdbClient">The TMDB client (lists the aired episodes a season or series must contain).</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public RequestReconciler(
    IRequestStore store,
    ILibraryMatcher libraryMatcher,
    INotificationService notificationService,
    IDownloadDispatcher dispatcher,
    ITmdbClient tmdbClient,
    Func<PluginConfiguration> config,
    ILogger<RequestReconciler> logger)
  {
    _store = store;
    _libraryMatcher = libraryMatcher;
    _notificationService = notificationService;
    _dispatcher = dispatcher;
    _tmdbClient = tmdbClient;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var now = DateTime.UtcNow;
    var justAvailable = new List<RequestRecord>();
    var reverted = 0;
    var repointed = 0;

    foreach (var request in all)
    {
      cancellationToken.ThrowIfCancellationRequested();

      // Grant ownership to EVERY active requester once the media is present: not just Approved
      // requests but also still-Pending ones (e.g. several people requested the same unreleased title
      // — each must own it on release, without waiting for individual approval, since it's already
      // in the library and shared ownership adds no new download).
      if (request.Status is RequestStatus.Approved or RequestStatus.Pending)
      {
        var itemId = ResolveItemId(request);
        if (itemId is not null && await IsDeliveredAsync(request, now, cancellationToken).ConfigureAwait(false))
        {
          await _store.MarkAvailableAsync(request.Id, itemId, cancellationToken).ConfigureAwait(false);

          // The media is present in Jellyfin but may have been placed manually (the backend failed to grab
          // it, or the user side-loaded it). If this request went through a backend, nudge it to rescan the
          // folder from disk so it imports the file and stops searching. Best-effort.
          if (request.DispatchedAt is not null)
          {
            await _dispatcher.RescanAsync(request, cancellationToken).ConfigureAwait(false);
          }

          // Defer the "now available" notification so episodes of the same season dropping together can be
          // grouped into one message per channel (see the batch below) instead of one per episode.
          justAvailable.Add(request);
        }
      }
      else if (request.Status == RequestStatus.Available && request.DeletionRequestedAt is null)
      {
        var live = ResolveItemId(request);
        if (live is null)
        {
          // The media is gone (e.g. deleted by another user / removed externally): no longer available.
          await _store.UpdateStatusAsync(request.Id, RequestStatus.Approved, request.DecidedBy ?? Guid.Empty, cancellationToken).ConfigureAwait(false);
          reverted++;
        }
        else
        {
          if (!string.Equals(live, request.JellyfinItemId, StringComparison.OrdinalIgnoreCase))
          {
            // The media is still there, but under a NEW item id: a library rescan or a metadata refresh
            // regenerates Jellyfin's GUIDs. The stored id then dangles, and a later deletion silently finds
            // nothing. Re-point the request at the live item (its ownership clock is untouched).
            await _store.SetJellyfinItemIdAsync(request.Id, live, cancellationToken).ConfigureAwait(false);
            repointed++;
          }

          await TrackArrivalsAsync(request, now, cancellationToken).ConfigureAwait(false);
        }
      }
    }

    // One grouped notification per (requester, title, season): several episodes of a season that become
    // available in the same pass produce a single message per channel rather than one per episode.
    foreach (var group in justAvailable.GroupBy(r => (r.UserId, r.TmdbId, r.Season)))
    {
      await _notificationService.NotifyAvailableBatchAsync(group.ToList(), cancellationToken).ConfigureAwait(false);
    }

    var resolved = justAvailable.Count;
    if (resolved > 0 || reverted > 0 || repointed > 0)
    {
      _logger.LogInformation(
        "Jelly Crowd reconcile: {Resolved} available, {Reverted} reverted, {Repointed} re-pointed at a new library item id.",
        resolved,
        reverted,
        repointed);
    }

    return resolved;
  }

  // A season or a whole series, as opposed to a movie or one named episode.
  private static bool IsMultiEpisode(RequestRecord request)
    => string.Equals(request.MediaType, "tv", StringComparison.Ordinal) && request.Episode is null;

  // A movie or a single episode is delivered as soon as it is in the library. A season or a whole series is
  // delivered once every aired episode is — or once some are and none has arrived for the grace period.
  // Marking it available on its first episode released its quota reservation while the rest was still
  // downloading, started its expiry clock early and made a series missing a season look complete.
  private async Task<bool> IsDeliveredAsync(RequestRecord request, DateTime now, CancellationToken cancellationToken)
  {
    if (!IsMultiEpisode(request))
    {
      return true;
    }

    var present = _libraryMatcher.ListEpisodeKeys(request.TmdbId, request.Season) ?? Array.Empty<EpisodeKey>();
    if (present.Count == 0)
    {
      return false;
    }

    // The grace period runs from the most recent arrival, so a season still trickling in keeps waiting.
    var progressAt = request.ProgressAt;
    if (present.Count > request.PresentEpisodes || progressAt is null)
    {
      progressAt = now;
      await _store.RecordProgressAsync(request.Id, present.Count, now, restartOwnershipClock: false, cancellationToken).ConfigureAwait(false);
    }

    var aired = await ListAiredEpisodesAsync(request, now, cancellationToken).ConfigureAwait(false);
    var grace = TimeSpan.FromHours(Math.Max(1, _config().PartialAvailabilityGraceHours));
    return SeasonCompletion.ShouldPromote(present, aired, progressAt, now, grace);
  }

  // For a fulfilled season or series, restart the ownership clock whenever a new episode arrives, so the
  // newest episode is kept for the full retention period instead of expiring along with the first one.
  private async Task TrackArrivalsAsync(RequestRecord request, DateTime now, CancellationToken cancellationToken)
  {
    if (!IsMultiEpisode(request))
    {
      return;
    }

    var count = (_libraryMatcher.ListEpisodeKeys(request.TmdbId, request.Season) ?? Array.Empty<EpisodeKey>()).Count;
    if (count <= request.PresentEpisodes)
    {
      return;
    }

    // A request fulfilled before arrivals were tracked has no baseline yet: record one without restarting
    // its clock, or every existing season would have its expiry pushed back at once.
    await _store.RecordProgressAsync(request.Id, count, now, restartOwnershipClock: request.PresentEpisodes > 0, cancellationToken).ConfigureAwait(false);
  }

  // The aired episodes a season or series request must contain, from TMDB. Null when TMDB cannot list them:
  // only the grace period can then settle the request, which merely delays it, never blocks it.
  private async Task<IReadOnlyCollection<EpisodeKey>?> ListAiredEpisodesAsync(RequestRecord request, DateTime now, CancellationToken cancellationToken)
  {
    try
    {
      var seasonNumbers = new List<int>();
      if (request.Season is int season)
      {
        seasonNumbers.Add(season);
      }
      else
      {
        foreach (var listed in await _tmdbClient.GetSeasonsAsync(request.TmdbId, "en-US", cancellationToken).ConfigureAwait(false))
        {
          if (listed.SeasonNumber > 0)
          {
            seasonNumbers.Add(listed.SeasonNumber);
          }
        }
      }

      var episodes = new List<Episode>();
      foreach (var number in seasonNumbers)
      {
        episodes.AddRange(await _tmdbClient.GetSeasonEpisodesAsync(request.TmdbId, number, "en-US", cancellationToken).ConfigureAwait(false));
      }

      return episodes.Count == 0 ? null : SeasonCompletion.AiredEpisodes(episodes, now, request.Season);
    }
#pragma warning disable CA1031 // A TMDB failure must not break reconciliation; the grace period still settles the request.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not list the episodes of {Title} from TMDB.", request.Title);
      return null;
    }
  }

  // For a per-episode / per-season TV request, availability means that exact episode (or season) is in
  // the library — not merely the series. Whole-show and movie requests match at the title level.
  private string? ResolveItemId(RequestRecord request)
  {
    if (string.Equals(request.MediaType, "tv", StringComparison.Ordinal) && request.Season.HasValue)
    {
      return _libraryMatcher.FindEpisodeItemId(request.TmdbId, request.Season, request.Episode);
    }

    return _libraryMatcher.FindItemId(request.MediaType, request.TmdbId);
  }
}
