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
  private readonly IRequestStore _store;
  private readonly IMediaDeleter _mediaDeleter;
  private readonly Func<PluginConfiguration> _configurationProvider;
  private readonly ILogger<DeletionTask> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="DeletionTask"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="mediaDeleter">The media deleter.</param>
  /// <param name="configurationProvider">Provides the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public DeletionTask(IRequestStore store, IMediaDeleter mediaDeleter, Func<PluginConfiguration> configurationProvider, ILogger<DeletionTask> logger)
  {
    _store = store;
    _mediaDeleter = mediaDeleter;
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

      // Only remove the file when no other active request still wants this title (shared media).
      var sharedWithOthers = await _store.AnyActiveReferenceAsync(request.Id, request.TmdbId, request.MediaType, cancellationToken).ConfigureAwait(false);
      if (!sharedWithOthers && !string.IsNullOrEmpty(request.JellyfinItemId))
      {
        _mediaDeleter.Delete(request.JellyfinItemId);
      }

      await _store.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
      deleted++;
      progress.Report((double)(i + 1) / due.Count * 100);
    }

    if (deleted > 0)
    {
      _logger.LogInformation("Jelly Crowd deletion task: removed {Count} flagged item(s).", deleted);
    }

    progress.Report(100);
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
