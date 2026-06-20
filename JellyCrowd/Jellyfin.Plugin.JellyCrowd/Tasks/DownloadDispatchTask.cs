using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Tasks;

/// <summary>
/// Scheduled backstop that dispatches approved requests to the configured download backend once
/// they are due (immediate dispatch also happens on approval; this catches deferred desired dates
/// and earlier failures).
/// </summary>
public sealed class DownloadDispatchTask : IScheduledTask
{
  private readonly IDownloadDispatcher _dispatcher;

  /// <summary>
  /// Initializes a new instance of the <see cref="DownloadDispatchTask"/> class.
  /// </summary>
  /// <param name="dispatcher">The download dispatcher.</param>
  public DownloadDispatchTask(IDownloadDispatcher dispatcher)
  {
    _dispatcher = dispatcher;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd: dispatch downloads";

  /// <inheritdoc />
  public string Key => "JellyCrowdDispatchDownloads";

  /// <inheritdoc />
  public string Description => "Sends approved Jelly Crowd requests to the configured download backend once their desired time has arrived.";

  /// <inheritdoc />
  public string Category => "Jelly Crowd";

  /// <inheritdoc />
  public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(progress);
    progress.Report(0);
    await _dispatcher.DispatchDueAsync(cancellationToken).ConfigureAwait(false);
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
        IntervalTicks = TimeSpan.FromMinutes(5).Ticks
      }
    };
  }
}
