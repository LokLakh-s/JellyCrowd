using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;

namespace Jellyfin.Plugin.JellyCrowd.Tests.TestDoubles;

/// <summary>
/// An <see cref="IDownloadDispatcher"/> double that records the cancellations and purges it is asked for.
/// </summary>
internal sealed class RecordingDownloadDispatcher : IDownloadDispatcher
{
  public List<RequestRecord> Cancelled { get; } = new();

  public List<(RequestRecord Request, bool LibraryDeletesFiles)> Purged { get; } = new();

  /// <summary>Gets or sets a value indicating whether purges succeed.</summary>
  public bool PurgeSucceeds { get; set; } = true;

  public Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(false);

  public Task DispatchDueAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  public Task TestActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  public Task CancelAsync(RequestRecord request, CancellationToken cancellationToken)
  {
    Cancelled.Add(request);
    return Task.CompletedTask;
  }

  public Task<bool> PurgeAsync(RequestRecord request, bool libraryDeletesFiles, CancellationToken cancellationToken)
  {
    Purged.Add((request, libraryDeletesFiles));
    return Task.FromResult(PurgeSucceeds);
  }

  public Task<bool> RetryAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(true);

  public Task RetryStuckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  public Task RescanAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;
}
