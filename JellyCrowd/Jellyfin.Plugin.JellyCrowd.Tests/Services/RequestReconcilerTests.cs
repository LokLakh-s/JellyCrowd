using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestReconciler"/>, over a real <see cref="JsonRequestStore"/>.
/// </summary>
public sealed class RequestReconcilerTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-rec-" + Guid.NewGuid().ToString("N") + ".json");
  private readonly JsonRequestStore _store;
  private readonly Mock<ILibraryMatcher> _matcher = new();

  public RequestReconcilerTests() => _store = new JsonRequestStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private RequestReconciler Create() => new(
    _store,
    _matcher.Object,
    Mock.Of<INotificationService>(),
    Mock.Of<IDownloadDispatcher>(),
    NullLogger<RequestReconciler>.Instance);

  private void LibraryHas(string? itemId)
    => _matcher.Setup(m => m.FindItemId(It.IsAny<string>(), It.IsAny<int>())).Returns(itemId);

  private async Task<RequestRecord> SeedAvailableAsync(string itemId)
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 42, MediaType = "movie", Title = "Shtisel" },
      CancellationToken.None);
    return (await _store.MarkAvailableAsync(created.Id, itemId, CancellationToken.None))!;
  }

  [Fact]
  public async Task Reconcile_ApprovedTitleNowInLibrary_BecomesAvailable()
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 42, MediaType = "movie", Title = "X", Status = RequestStatus.Approved },
      CancellationToken.None);
    LibraryHas("item-1");

    Assert.Equal(1, await Create().ReconcileAsync(CancellationToken.None));

    var stored = await _store.GetByIdAsync(created.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Available, stored!.Status);
    Assert.Equal("item-1", stored.JellyfinItemId);
  }

  // The reported case: the media is still there, but a library rescan / metadata refresh regenerated its
  // item id. The stored id then dangles and a later deletion silently finds nothing.
  [Fact]
  public async Task Reconcile_ItemIdChanged_RepointsTheRequest_WithoutRestartingOwnership()
  {
    var available = await SeedAvailableAsync("stale-id");
    var ownedSince = available.AvailableAt;
    LibraryHas("regenerated-id");

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(available.Id, CancellationToken.None);
    Assert.Equal("regenerated-id", stored!.JellyfinItemId);
    Assert.Equal(RequestStatus.Available, stored.Status); // still owned...
    Assert.Equal(ownedSince, stored.AvailableAt);         // ...and the expiry countdown did not restart
  }

  [Fact]
  public async Task Reconcile_ItemIdUnchanged_LeavesTheRequestAlone()
  {
    var available = await SeedAvailableAsync("item-1");
    LibraryHas("item-1");

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(available.Id, CancellationToken.None);
    Assert.Equal("item-1", stored!.JellyfinItemId);
    Assert.Equal(RequestStatus.Available, stored.Status);
  }

  [Fact]
  public async Task Reconcile_MediaGone_RevertsToApproved()
  {
    var available = await SeedAvailableAsync("item-1");
    LibraryHas(null);

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(available.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, stored!.Status);
  }
}
