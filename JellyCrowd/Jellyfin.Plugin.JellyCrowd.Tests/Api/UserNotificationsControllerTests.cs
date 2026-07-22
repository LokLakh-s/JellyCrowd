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
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly string _prefsPath = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
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

  private UserNotificationsController CreateController(INotificationService? notifications = null)
    => new(_store, _prefs, new FakeUserAccessor(), new Services.NoOpActivityLog(), notifications ?? new TestNotifier(), _ => "tester")
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

  // Records the personal test delivery, or fails it on demand, without touching SMTP.
  private sealed class TestNotifier : INotificationService
  {
    private readonly Exception? _failure;

    public TestNotifier(Exception? failure = null) => _failure = failure;

    public Guid? TestedUser { get; private set; }

    public Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task NotifyAvailableBatchAsync(System.Collections.Generic.IReadOnlyList<RequestRecord> requests, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task NotifyPersonalAsync(Guid userId, PersonalNotifyKind kind, string title, string subject, string body, string? posterPath, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SendTestAsync(string channel, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SendPersonalTestAsync(Guid userId, CancellationToken cancellationToken)
    {
      TestedUser = userId;
      return _failure is null ? Task.CompletedTask : Task.FromException(_failure);
    }
  }

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
      new UserNotificationPrefs { UserId = Guid.NewGuid(), Enabled = false, Email = "u@example.com", NtfyTopic = "t" },
      CancellationToken.None);

    var prefs = await _prefs.GetAsync(User, CancellationToken.None);
    Assert.False(prefs.Enabled);
    Assert.Equal("u@example.com", prefs.Email);
    Assert.Equal("t", prefs.NtfyTopic);
  }

  [Fact]
  public async Task SetPrefs_BlankEmail_TurnsEmailOff()
  {
    var result = await CreateController().SetPrefs(
      new UserNotificationPrefs { Email = "   ", NtfyTopic = "t" },
      CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
    var prefs = await _prefs.GetAsync(User, CancellationToken.None);
    Assert.Null(prefs.Email);
  }

  [Theory]
  [InlineData("not-an-email")]
  [InlineData("someone@localhost")]        // no dot in the domain
  [InlineData("has space@example.com")]
  [InlineData("two@@example.com")]
  [InlineData("victim@example.com\nBcc: spam@evil.com")] // header-injection shaped input
  public async Task SetPrefs_InvalidEmail_IsRejected_AndNotStored(string email)
  {
    var result = await CreateController().SetPrefs(
      new UserNotificationPrefs { Email = email },
      CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
    var prefs = await _prefs.GetAsync(User, CancellationToken.None);
    Assert.Null(prefs.Email); // never reached the store
  }

  [Fact]
  public async Task TestMine_DeliversToTheCallersOwnChannels()
  {
    var notifier = new TestNotifier();

    var result = await CreateController(notifier).TestMine(CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
    Assert.Equal(User, notifier.TestedUser); // the caller's id, never one from the request body
  }

  [Fact]
  public async Task TestMine_WhenDeliveryFails_ReturnsTheReason()
  {
    // A test that fails quietly is worse than no test: the user must see why nothing arrived.
    var notifier = new TestNotifier(new InvalidOperationException("Set an e-mail address or an ntfy topic first."));

    var result = await CreateController(notifier).TestMine(CancellationToken.None);

    var problem = Assert.IsType<ObjectResult>(result);
    Assert.Equal(400, problem.StatusCode);
    Assert.Contains("ntfy topic", Assert.IsType<ProblemDetails>(problem.Value).Detail, StringComparison.Ordinal);
  }

  [Fact]
  public async Task TestMine_IncludesTheInnerCauseOfAnSmtpFailure()
  {
    var notifier = new TestNotifier(new InvalidOperationException("Sending failed.", new Exception("authentication failed")));

    var result = await CreateController(notifier).TestMine(CancellationToken.None);

    var detail = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Detail;
    Assert.Contains("Sending failed. → authentication failed", detail, StringComparison.Ordinal);
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(false);
  }
}
