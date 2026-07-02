using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Tasks;

/// <summary>
/// Scheduled task that deletes media users flagged for deletion once the configured retention has elapsed.
/// </summary>
public sealed class DeletionTask : IScheduledTask
{
  // How long to keep retrying a failed backend purge before giving up and deleting locally anyway, so a
  // permanently-removed/misconfigured backend can never strand a flagged request forever.
  private static readonly TimeSpan PurgeRetryGrace = TimeSpan.FromDays(7);

  private readonly IRequestStore _store;
  private readonly IMediaDeleter _mediaDeleter;
  private readonly IDownloadDispatcher _downloadDispatcher;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly INotificationService _notificationService;
  private readonly IQuotaHoldPromoter _quotaHoldPromoter;
  private readonly IEmptyLibraryCleaner _emptyLibraryCleaner;
  private readonly Func<PluginConfiguration> _configurationProvider;
  private readonly ILogger<DeletionTask> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="DeletionTask"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="mediaDeleter">The media deleter.</param>
  /// <param name="downloadDispatcher">The download dispatcher (to purge the backend on full deletion).</param>
  /// <param name="libraryMatcher">The library matcher (to resolve the season/episode item to delete).</param>
  /// <param name="notificationService">The notification service, used to warn owners on expiry.</param>
  /// <param name="quotaHoldPromoter">The quota-hold promoter (resumes held requests once space is freed).</param>
  /// <param name="emptyLibraryCleaner">Removes empty series (ghosts) left after deletion.</param>
  /// <param name="configurationProvider">Provides the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public DeletionTask(IRequestStore store, IMediaDeleter mediaDeleter, IDownloadDispatcher downloadDispatcher, ILibraryMatcher libraryMatcher, INotificationService notificationService, IQuotaHoldPromoter quotaHoldPromoter, IEmptyLibraryCleaner emptyLibraryCleaner, Func<PluginConfiguration> configurationProvider, ILogger<DeletionTask> logger)
  {
    _store = store;
    _mediaDeleter = mediaDeleter;
    _downloadDispatcher = downloadDispatcher;
    _libraryMatcher = libraryMatcher;
    _notificationService = notificationService;
    _quotaHoldPromoter = quotaHoldPromoter;
    _emptyLibraryCleaner = emptyLibraryCleaner;
    _configurationProvider = configurationProvider;
    _logger = logger;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd: process deletions";

  /// <inheritdoc />
  public string Key => "JellyCrowdProcessDeletions";

  /// <inheritdoc />
  public string Description => "Deletes media that users flagged for deletion once the retention period has elapsed.";

  /// <inheritdoc />
  public string Category => "Jelly Crowd";

  /// <inheritdoc />
  public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(progress);

    var retentionHours = _configurationProvider().DeletionRetentionHours;
    if (retentionHours < 0)
    {
      retentionHours = 0;
    }

    var cutoff = DateTime.UtcNow - TimeSpan.FromHours(retentionHours);
    var due = await _store.GetDueForDeletionAsync(cutoff, cancellationToken).ConfigureAwait(false);
    var deleted = 0;

    for (var i = 0; i < due.Count; i++)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var request = due[i];

      // Only remove the media when no other active request still wants this title (shared media).
      var sharedWithOthers = await _store.AnyActiveReferenceAsync(request.Id, request.TmdbId, request.MediaType, cancellationToken).ConfigureAwait(false);
      if (!sharedWithOthers)
      {
        // Purge from the download backend (Radarr movie / whole Sonarr series + active downloads) so a
        // future re-request starts clean. If the backend can't be reached, the purge reports failure:
        // keep the request flagged and retry next run (N18 integrity), unless we've waited past the grace.
        var purged = await _downloadDispatcher.PurgeAsync(request, cancellationToken).ConfigureAwait(false);
        if (!purged && !PurgeGraceElapsed(request, retentionHours))
        {
          _logger.LogWarning(
            "Jelly Crowd deletion: backend purge failed for {Title}; leaving it flagged to retry.",
            request.Title);
          continue;
        }

        // Delete the right Jellyfin item: a season request removes the whole Season folder, an episode
        // request just that episode, a movie/whole-series request the stored item.
        var itemId = ResolveDeletionItemId(request);
        if (!string.IsNullOrEmpty(itemId))
        {
          _mediaDeleter.Delete(itemId);
        }
      }

      await _store.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
      deleted++;
      progress.Report((double)(i + 1) / due.Count * 100);
    }

    if (deleted > 0)
    {
      _logger.LogInformation("Jelly Crowd deletion task: removed {Count} flagged item(s).", deleted);
    }

    // Lapse ownerships whose expiry window has elapsed (frees quota; never deletes the file).
    var expiryDays = _configurationProvider().MediaExpiryDays;
    if (expiryDays > 0)
    {
      var expiryCutoff = DateTime.UtcNow - TimeSpan.FromDays(expiryDays);
      var lapsed = await _store.ExpireOwnershipsAsync(expiryCutoff, cancellationToken).ConfigureAwait(false);
      if (lapsed.Count > 0)
      {
        _logger.LogInformation("Jelly Crowd expiry: lapsed {Count} ownership(s).", lapsed.Count);
        foreach (var record in lapsed)
        {
          var body = $"\"{record.Title}\" has left your library after {expiryDays} days (your quota is freed). Re-add it from the catalog if you still want it.";
          await _notificationService.NotifyPersonalAsync(
            record.UserId,
            Models.PersonalNotifyKind.QuotaExpiry,
            record.Title,
            "Media expired from your library",
            body,
            record.PosterPath,
            cancellationToken).ConfigureAwait(false);
        }
      }
    }

    // Sweep away empty series (0 episodes) that Jellyfin keeps in the library after their files were
    // deleted. Skip any title an active request still wants, and any series too new to be a settled ghost.
    if (_configurationProvider().RemoveEmptySeries)
    {
      var wanted = new HashSet<int>();
      foreach (var record in await _store.GetAllAsync(cancellationToken).ConfigureAwait(false))
      {
        if (record.Status is Models.RequestStatus.Pending or Models.RequestStatus.Approved)
        {
          wanted.Add(record.TmdbId);
        }
      }

      var ghosts = _emptyLibraryCleaner.RemoveEmptySeries(_configurationProvider().EmptySeriesMinAgeHours, wanted);
      if (ghosts > 0)
      {
        _logger.LogInformation("Jelly Crowd removed {Count} empty series.", ghosts);
      }
    }

    // Deleting media and lapsing ownerships both free disk quota: resume any requests that were held
    // back purely because the requester was over quota.
    await _quotaHoldPromoter.PromoteAsync(cancellationToken).ConfigureAwait(false);

    progress.Report(100);
  }

  // The Jellyfin item to delete: a season folder for a per-season request, the episode for a per-episode
  // request, otherwise the stored item (movie / whole series). Falls back to the stored id when the
  // season/episode item can't be resolved.
  private string? ResolveDeletionItemId(Models.RequestRecord request)
  {
    if (string.Equals(request.MediaType, "tv", StringComparison.Ordinal) && request.Season is int season)
    {
      if (request.Episode is int episode)
      {
        return _libraryMatcher.FindEpisodeItemId(request.TmdbId, season, episode) ?? request.JellyfinItemId;
      }

      return _libraryMatcher.FindSeasonItemId(request.TmdbId, season) ?? request.JellyfinItemId;
    }

    return request.JellyfinItemId;
  }

  private static bool PurgeGraceElapsed(Models.RequestRecord request, int retentionHours)
  {
    if (request.DeletionRequestedAt is not { } requestedAt)
    {
      return true; // no timestamp to reason about — don't get stuck.
    }

    return DateTime.UtcNow - requestedAt > TimeSpan.FromHours(retentionHours) + PurgeRetryGrace;
  }

  /// <inheritdoc />
  public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
  {
    return new[]
    {
      new TaskTriggerInfo
      {
        Type = TaskTriggerInfoType.IntervalTrigger,
        IntervalTicks = TimeSpan.FromHours(1).Ticks
      }
    };
  }
}
