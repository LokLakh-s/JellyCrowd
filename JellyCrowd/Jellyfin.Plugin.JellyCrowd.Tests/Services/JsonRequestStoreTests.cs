using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonRequestStore"/>.
/// </summary>
public sealed class JsonRequestStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;

  public JsonRequestStoreTests()
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
  public async Task CreateAsync_AssignsIdStatusAndTimestamp()
  {
    var created = await _store.CreateAsync(NewRecord(Guid.NewGuid()), CancellationToken.None);

    Assert.NotEqual(Guid.Empty, created.Id);
    Assert.Equal(RequestStatus.Pending, created.Status);
    Assert.NotEqual(default, created.RequestedAt);
  }

  [Fact]
  public async Task GetByUserAsync_ReturnsOnlyThatUser()
  {
    var alice = Guid.NewGuid();
    var bob = Guid.NewGuid();
    await _store.CreateAsync(NewRecord(alice, 1), CancellationToken.None);
    await _store.CreateAsync(NewRecord(bob, 2), CancellationToken.None);

    var mine = await _store.GetByUserAsync(alice, CancellationToken.None);

    Assert.Single(mine);
    Assert.Equal(alice, mine[0].UserId);
  }

  [Fact]
  public async Task ExistsActiveAsync_TrueForPending_FalseForDenied()
  {
    var user = Guid.NewGuid();
    var created = await _store.CreateAsync(NewRecord(user, 42), CancellationToken.None);

    Assert.True(await _store.ExistsActiveAsync(user, 42, "movie", null, CancellationToken.None));

    await _store.UpdateStatusAsync(created.Id, RequestStatus.Denied, Guid.NewGuid(), CancellationToken.None);

    Assert.False(await _store.ExistsActiveAsync(user, 42, "movie", null, CancellationToken.None));
  }

  [Fact]
  public async Task UpdateStatusAsync_SetsStatusAndDecider()
  {
    var admin = Guid.NewGuid();
    var created = await _store.CreateAsync(NewRecord(Guid.NewGuid()), CancellationToken.None);

    var updated = await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, admin, CancellationToken.None);

    Assert.NotNull(updated);
    Assert.Equal(RequestStatus.Approved, updated!.Status);
    Assert.Equal(admin, updated.DecidedBy);
    Assert.NotNull(updated.DecidedAt);
  }

  [Fact]
  public async Task UpdateStatusAsync_UnknownId_ReturnsNull()
  {
    Assert.Null(await _store.UpdateStatusAsync(Guid.NewGuid(), RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None));
  }

  [Fact]
  public async Task CountUserRequestsSinceAsync_CountsRecentNonDenied()
  {
    var user = Guid.NewGuid();
    await _store.CreateAsync(NewRecord(user, 1), CancellationToken.None);
    var denied = await _store.CreateAsync(NewRecord(user, 2), CancellationToken.None);
    await _store.UpdateStatusAsync(denied.Id, RequestStatus.Denied, Guid.NewGuid(), CancellationToken.None);

    var past = DateTime.UtcNow.AddDays(-7);
    var future = DateTime.UtcNow.AddDays(1);

    Assert.Equal(1, await _store.CountUserRequestsSinceAsync(user, past, CancellationToken.None));
    Assert.Equal(0, await _store.CountUserRequestsSinceAsync(user, future, CancellationToken.None));
  }

  [Fact]
  public async Task Persistence_SurvivesNewInstance()
  {
    await _store.CreateAsync(NewRecord(Guid.NewGuid()), CancellationToken.None);

    var reopened = new JsonRequestStore(_path);
    try
    {
      var all = await reopened.GetAllAsync(CancellationToken.None);
      Assert.Single(all);
    }
    finally
    {
      reopened.Dispose();
    }
  }

  private static RequestRecord NewRecord(Guid userId, int tmdbId = 1)
    => new() { UserId = userId, TmdbId = tmdbId, MediaType = "movie", Title = "Test" };
}
