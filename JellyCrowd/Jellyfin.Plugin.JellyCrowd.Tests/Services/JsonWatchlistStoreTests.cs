using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonWatchlistStore"/>.
/// </summary>
public sealed class JsonWatchlistStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonWatchlistStore _store;

  public JsonWatchlistStoreTests()
  {
    _store = new JsonWatchlistStore(_path);
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
  public async Task AddAsync_AssignsIdAndTimestamp_AndIsIdempotent()
  {
    var user = Guid.NewGuid();
    var first = await _store.AddAsync(NewEntry(user, 5), CancellationToken.None);
    Assert.NotEqual(Guid.Empty, first.Id);
    Assert.NotEqual(default, first.AddedAt);

    var again = await _store.AddAsync(NewEntry(user, 5), CancellationToken.None);
    Assert.Equal(first.Id, again.Id);

    var all = await _store.GetByUserAsync(user, CancellationToken.None);
    Assert.Single(all);
  }

  [Fact]
  public async Task RemoveAsync_RemovesMatching_ReturnsFalseWhenAbsent()
  {
    var user = Guid.NewGuid();
    await _store.AddAsync(NewEntry(user, 5), CancellationToken.None);

    Assert.True(await _store.RemoveAsync(user, 5, "movie", CancellationToken.None));
    Assert.Empty(await _store.GetByUserAsync(user, CancellationToken.None));
    Assert.False(await _store.RemoveAsync(user, 5, "movie", CancellationToken.None));
  }

  [Fact]
  public async Task GetByUserAsync_ReturnsOnlyThatUser()
  {
    var alice = Guid.NewGuid();
    var bob = Guid.NewGuid();
    await _store.AddAsync(NewEntry(alice, 1), CancellationToken.None);
    await _store.AddAsync(NewEntry(bob, 2), CancellationToken.None);

    var mine = await _store.GetByUserAsync(alice, CancellationToken.None);

    Assert.Single(mine);
    Assert.Equal(alice, mine[0].UserId);
  }

  private static WatchlistEntry NewEntry(Guid userId, int tmdbId)
    => new() { UserId = userId, TmdbId = tmdbId, MediaType = "movie", Title = "Test" };
}
