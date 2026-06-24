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
  private readonly INotificationService _notificationService;
  private readonly Func<PluginConfiguration> _configurationProvider;
  private readonly ILogger<DeletionTask> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="DeletionTask"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="mediaDeleter">The media deleter.</param>
  /// <param name="downloadDispatcher">The download dispatcher (to purge the backend on full deletion).</param>
  /// <param name="notificationService">The notification service, used to warn owners on expiry.</param>
  /// <param name="configurationProvider">Provides the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public DeletionTask(IRequestStore store, IMediaDeleter mediaDeleter, IDownloadDispatcher downloadDispatcher, INotificationService notificationService, Func<PluginConfiguration> configurationProvider, ILogger<DeletionTask> logger)
  {
    _store = store;
    _mediaDeleter = mediaDeleter;
    _downloadDispatcher = downloadDispatcher;
    _notificationService = notificationService;
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

        if (!string.IsNullOrEmpty(request.JellyfinItemId))
        {
          _mediaDeleter.Delete(request.JellyfinItemId);
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

    progress.Report(100);
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
