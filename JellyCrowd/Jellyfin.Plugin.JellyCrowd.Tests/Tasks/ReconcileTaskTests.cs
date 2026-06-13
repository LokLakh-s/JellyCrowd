using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Tasks;

/// <summary>
/// Tests for <see cref="RequestReconciler"/>.
/// </summary>
public sealed class ReconcileTaskTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;

  public ReconcileTaskTests()
  {
    _store = new JsonRequestStore(_path);
  }

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  [Fact]
  public async Task Execute_MarksApprovedAvailable_WhenInLibrary()
  {
    var id = await SeedApprovedAsync();
    var reconciler = new RequestReconciler(_store, new StubMatcher(true), new NoopNotificationService(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    var updated = await _store.GetByIdAsync(id, CancellationToken.None);
    Assert.Equal(RequestStatus.Available, updated!.Status);
  }

  [Fact]
  public async Task Execute_LeavesApproved_WhenNotInLibrary()
  {
    var id = await SeedApprovedAsync();
    var reconciler = new RequestReconciler(_store, new StubMatcher(false), new NoopNotificationService(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    var updated = await _store.GetByIdAsync(id, CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, updated!.Status);
  }

  private async Task<Guid> SeedApprovedAsync()
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 5, MediaType = "movie", Title = "X" },
      CancellationToken.None);
    await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    return created.Id;
  }

  private sealed class StubMatcher : ILibraryMatcher
  {
    private readonly bool _result;

    public StubMatcher(bool result) => _result = result;

    public bool Exists(string mediaType, int tmdbId) => _result;

    public string? FindItemId(string mediaType, int tmdbId) => _result ? "x" : null;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;
  }

  private sealed class NoopNotificationService : INotificationService
  {
    public Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task SendTestAsync(string channel, CancellationToken cancellationToken) => Task.CompletedTask;
  }
}
