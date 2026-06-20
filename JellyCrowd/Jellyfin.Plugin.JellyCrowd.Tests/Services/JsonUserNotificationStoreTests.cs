using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonUserNotificationStore"/>.
/// </summary>
public sealed class JsonUserNotificationStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonUserNotificationStore _store;

  public JsonUserNotificationStoreTests() => _store = new JsonUserNotificationStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private static UserNotification New(Guid user, string title = "X") => new()
  {
    UserId = user,
    Event = "Available",
    Title = title,
    Message = "msg"
  };

  [Fact]
  public async Task AddAsync_AssignsIdAndTimestamp()
  {
    var added = await _store.AddAsync(New(Guid.NewGuid()), CancellationToken.None);

    Assert.NotEqual(Guid.Empty, added.Id);
    Assert.NotEqual(default, added.CreatedAt);
  }

  [Fact]
  public async Task GetByUserAsync_ReturnsOnlyThatUser()
  {
    var alice = Guid.NewGuid();
    await _store.AddAsync(New(alice), CancellationToken.None);
    await _store.AddAsync(New(Guid.NewGuid()), CancellationToken.None);

    var mine = await _store.GetByUserAsync(alice, CancellationToken.None);

    Assert.Single(mine);
    Assert.Equal(alice, mine[0].UserId);
  }

  [Fact]
  public async Task MarkReadAsync_All_MarksEveryUnread()
  {
    var user = Guid.NewGuid();
    await _store.AddAsync(New(user), CancellationToken.None);
    await _store.AddAsync(New(user), CancellationToken.None);

    var updated = await _store.MarkReadAsync(user, null, CancellationToken.None);

    Assert.Equal(2, updated);
    Assert.All(await _store.GetByUserAsync(user, CancellationToken.None), n => Assert.True(n.Read));
  }

  [Fact]
  public async Task ClearAsync_Single_RemovesOnlyThatOne()
  {
    var user = Guid.NewGuid();
    var a = await _store.AddAsync(New(user, "A"), CancellationToken.None);
    await _store.AddAsync(New(user, "B"), CancellationToken.None);

    var removed = await _store.ClearAsync(user, a.Id, CancellationToken.None);

    Assert.Equal(1, removed);
    var remaining = await _store.GetByUserAsync(user, CancellationToken.None);
    Assert.Single(remaining);
    Assert.Equal("B", remaining[0].Title);
  }

  [Fact]
  public async Task AddAsync_CapsToFiftyMostRecentPerUser()
  {
    var user = Guid.NewGuid();
    for (var i = 0; i < 55; i++)
    {
      await _store.AddAsync(New(user, "n" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), CancellationToken.None);
    }

    Assert.Equal(50, (await _store.GetByUserAsync(user, CancellationToken.None)).Count);
  }

  [Fact]
  public async Task AddAsync_PrunesEntriesOlderThanRetention()
  {
    var user = Guid.NewGuid();
    var old = New(user, "old");
    old.CreatedAt = DateTime.UtcNow.AddDays(-40);
    await _store.AddAsync(old, CancellationToken.None);
    await _store.AddAsync(New(user, "fresh"), CancellationToken.None);

    var mine = await _store.GetByUserAsync(user, CancellationToken.None);

    Assert.Single(mine);
    Assert.Equal("fresh", mine[0].Title);
  }
}
