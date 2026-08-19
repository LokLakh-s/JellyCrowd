using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonPlaybackHistoryStore"/>, focused on the per-user history operations that back
/// the personal "watched" screen.
/// </summary>
public sealed class JsonPlaybackHistoryStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-hist-" + Guid.NewGuid() + ".json");

  public void Dispose()
  {
    if (File.Exists(_path)) { File.Delete(_path); }
  }

  private static PlaybackRecord Rec(Guid user, string title, DateTime playedUtc) => new()
  {
    UserId = user,
    ItemName = title,
    ItemType = "Movie",
    PlayedAtUtc = playedUtc,
    Minutes = 42
  };

  [Fact]
  public async Task GetByUser_ReturnsOnlyThatUsersRecords_NewestFirst()
  {
    var store = new JsonPlaybackHistoryStore(_path);
    var alice = Guid.NewGuid();
    var bob = Guid.NewGuid();
    await store.AddAsync(Rec(alice, "Older", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
    await store.AddAsync(Rec(alice, "Newer", new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
    await store.AddAsync(Rec(bob, "Bob's", new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

    var mine = await store.GetByUserAsync(alice, 0, CancellationToken.None);

    Assert.Equal(new[] { "Newer", "Older" }, mine.Select(r => r.ItemName));
    Assert.DoesNotContain(mine, r => r.UserId == bob);
  }

  [Fact]
  public async Task GetByUser_HonorsTheLimit()
  {
    var store = new JsonPlaybackHistoryStore(_path);
    var user = Guid.NewGuid();
    for (var i = 0; i < 5; i++)
    {
      await store.AddAsync(Rec(user, "E" + i, new DateTime(2024, 1, 1 + i, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
    }

    var limited = await store.GetByUserAsync(user, 2, CancellationToken.None);

    Assert.Equal(2, limited.Count);
    Assert.Equal(new[] { "E4", "E3" }, limited.Select(r => r.ItemName)); // the two most recent
  }

  [Fact]
  public async Task History_IsNeverAutoErasedByAge()
  {
    // The defining requirement: a very old record stays until the user removes it themselves.
    var store = new JsonPlaybackHistoryStore(_path);
    var user = Guid.NewGuid();
    await store.AddAsync(Rec(user, "Ancient", new DateTime(2005, 1, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
    await store.AddAsync(Rec(user, "Recent", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

    var mine = await store.GetByUserAsync(user, 0, CancellationToken.None);

    Assert.Contains(mine, r => r.ItemName == "Ancient");
  }

  [Fact]
  public async Task DeleteByUser_RemovesOnlyThatUsersRecords()
  {
    var store = new JsonPlaybackHistoryStore(_path);
    var alice = Guid.NewGuid();
    var bob = Guid.NewGuid();
    await store.AddAsync(Rec(alice, "A1", DateTime.UtcNow), CancellationToken.None);
    await store.AddAsync(Rec(alice, "A2", DateTime.UtcNow), CancellationToken.None);
    await store.AddAsync(Rec(bob, "B1", DateTime.UtcNow), CancellationToken.None);

    var removed = await store.DeleteByUserAsync(alice, CancellationToken.None);

    Assert.Equal(2, removed);
    Assert.Empty(await store.GetByUserAsync(alice, 0, CancellationToken.None));
    Assert.Single(await store.GetByUserAsync(bob, 0, CancellationToken.None)); // Bob is untouched
  }

  [Fact]
  public async Task DeleteOne_RemovesTheEntry_ButOnlyForItsOwner()
  {
    var store = new JsonPlaybackHistoryStore(_path);
    var alice = Guid.NewGuid();
    var bob = Guid.NewGuid();
    await store.AddAsync(Rec(alice, "A1", DateTime.UtcNow), CancellationToken.None);
    var aliceId = (await store.GetByUserAsync(alice, 0, CancellationToken.None))[0].Id;

    // Bob cannot delete Alice's entry even with its id.
    Assert.False(await store.DeleteOneAsync(bob, aliceId, CancellationToken.None));
    Assert.Single(await store.GetByUserAsync(alice, 0, CancellationToken.None));

    // Alice can.
    Assert.True(await store.DeleteOneAsync(alice, aliceId, CancellationToken.None));
    Assert.Empty(await store.GetByUserAsync(alice, 0, CancellationToken.None));
  }

  [Fact]
  public async Task DeleteByUser_OnEmptyHistory_ReturnsZero()
  {
    var store = new JsonPlaybackHistoryStore(_path);
    Assert.Equal(0, await store.DeleteByUserAsync(Guid.NewGuid(), CancellationToken.None));
  }
}
