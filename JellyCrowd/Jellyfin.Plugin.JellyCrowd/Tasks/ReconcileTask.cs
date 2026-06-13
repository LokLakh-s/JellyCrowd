using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Tasks;

/// <summary>
/// Scheduled backstop that reconciles approved requests against the library (real-time reconciliation
/// also happens on library item-added events).
/// </summary>
public sealed class ReconcileTask : IScheduledTask
{
  private readonly IRequestReconciler _reconciler;

  /// <summary>
  /// Initializes a new instance of the <see cref="ReconcileTask"/> class.
  /// </summary>
  /// <param name="reconciler">The request reconciler.</param>
  public ReconcileTask(IRequestReconciler reconciler)
  {
    _reconciler = reconciler;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd: reconcile requests";

  /// <inheritdoc />
  public string Key => "JellyCrowdReconcileRequests";

  /// <inheritdoc />
  public string Description => "Marks approved Jelly Crowd requests as available once the media appears in the library.";

  /// <inheritdoc />
  public string Category => "Jelly Crowd";

  /// <inheritdoc />
  public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(progress);
    progress.Report(0);
    await _reconciler.ReconcileAsync(cancellationToken).ConfigureAwait(false);
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
        IntervalTicks = TimeSpan.FromMinutes(15).Ticks
      }
    };
  }
}
