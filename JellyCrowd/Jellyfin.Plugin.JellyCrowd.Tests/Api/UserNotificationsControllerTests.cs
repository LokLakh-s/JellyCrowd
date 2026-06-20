using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="UserNotificationsController"/> using a real <see cref="JsonUserNotificationStore"/>.
/// </summary>
public sealed class UserNotificationsControllerTests : IDisposable
{
  private static readonly Guid User = Guid.NewGuid();
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly string _prefsPath = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonUserNotificationStore _store;
  private readonly JsonUserPrefsStore _prefs;

  public UserNotificationsControllerTests()
  {
    _store = new JsonUserNotificationStore(_path);
    _prefs = new JsonUserPrefsStore(_prefsPath);
  }

  public void Dispose()
  {
    _store.Dispose();
    _prefs.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }

    if (File.Exists(_prefsPath))
    {
      File.Delete(_prefsPath);
    }
  }

  private UserNotificationsController CreateController()
    => new(_store, _prefs, new FakeUserAccessor())
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

  private async Task SeedAsync(bool read)
  {
    var n = await _store.AddAsync(new UserNotification { UserId = User, Event = "Available", Title = "T", Message = "m" }, CancellationToken.None);
    if (read)
    {
      await _store.MarkReadAsync(User, n.Id, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Mine_ReturnsItemsAndUnreadCount()
  {
    await SeedAsync(read: true);
    await SeedAsync(read: false);

    var result = await CreateController().Mine(CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var dto = Assert.IsType<UserNotificationsDto>(ok.Value);
    Assert.Equal(2, dto.Items.Count);
    Assert.Equal(1, dto.Unread);
  }

  [Fact]
  public async Task MarkAllRead_ClearsUnread()
  {
    await SeedAsync(read: false);
    await SeedAsync(read: false);

    await CreateController().MarkAllRead(CancellationToken.None);

    var dto = Assert.IsType<UserNotificationsDto>(Assert.IsType<OkObjectResult>((await CreateController().Mine(CancellationToken.None)).Result).Value);
    Assert.Equal(0, dto.Unread);
  }

  [Fact]
  public async Task ClearAll_RemovesEverything()
  {
    await SeedAsync(read: false);

    await CreateController().ClearAll(CancellationToken.None);

    var dto = Assert.IsType<UserNotificationsDto>(Assert.IsType<OkObjectResult>((await CreateController().Mine(CancellationToken.None)).Result).Value);
    Assert.Empty(dto.Items);
  }

  [Fact]
  public async Task GetPrefs_DefaultsEnabled()
  {
    var result = await CreateController().GetPrefs(CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var prefs = Assert.IsType<UserNotificationPrefs>(ok.Value);
    Assert.True(prefs.Enabled);
  }

  [Fact]
  public async Task SetPrefs_PersistsAndForcesCurrentUser()
  {
    await CreateController().SetPrefs(
      new UserNotificationPrefs { UserId = Guid.NewGuid(), Enabled = false, Email = "u@example", NtfyTopic = "t" },
      CancellationToken.None);

    var prefs = await _prefs.GetAsync(User, CancellationToken.None);
    Assert.False(prefs.Enabled);
    Assert.Equal("u@example", prefs.Email);
    Assert.Equal("t", prefs.NtfyTopic);
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(false);
  }
}
