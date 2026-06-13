using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Reconciles requests in near real-time: when Jellyfin adds a library item, approved requests whose
/// media just arrived are flipped to available (debounced to avoid storms during library scans).
/// </summary>
public sealed class LibraryEventEntryPoint : IHostedService
{
  private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(20);

  private readonly ILibraryManager _libraryManager;
  private readonly IRequestReconciler _reconciler;
  private readonly ILogger<LibraryEventEntryPoint> _logger;
  private readonly object _gate = new();
  private DateTime _lastRun = DateTime.MinValue;

  /// <summary>
  /// Initializes a new instance of the <see cref="LibraryEventEntryPoint"/> class.
  /// </summary>
  /// <param name="libraryManager">The library manager.</param>
  /// <param name="reconciler">The request reconciler.</param>
  /// <param name="logger">The logger.</param>
  public LibraryEventEntryPoint(ILibraryManager libraryManager, IRequestReconciler reconciler, ILogger<LibraryEventEntryPoint> logger)
  {
    _libraryManager = libraryManager;
    _reconciler = reconciler;
    _logger = logger;
  }

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken)
  {
    _libraryManager.ItemAdded += OnLibraryChanged;
    _libraryManager.ItemRemoved += OnLibraryChanged;
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken)
  {
    _libraryManager.ItemAdded -= OnLibraryChanged;
    _libraryManager.ItemRemoved -= OnLibraryChanged;
    return Task.CompletedTask;
  }

  private void OnLibraryChanged(object? sender, ItemChangeEventArgs e)
  {
    lock (_gate)
    {
      if (DateTime.UtcNow - _lastRun < Debounce)
      {
        return;
      }

      _lastRun = DateTime.UtcNow;
    }

    _ = ReconcileSafeAsync();
  }

  private async Task ReconcileSafeAsync()
  {
    try
    {
      await _reconciler.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // A background reconcile failure must not crash the event handler.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd real-time reconcile failed.");
    }
  }
}
