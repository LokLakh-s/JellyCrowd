using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonUserPrefsStore"/>.
/// </summary>
public sealed class JsonUserPrefsStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonUserPrefsStore _store;

  public JsonUserPrefsStoreTests() => _store = new JsonUserPrefsStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  [Fact]
  public async Task GetAsync_DefaultsEnabledWhenNoneSaved()
  {
    var prefs = await _store.GetAsync(Guid.NewGuid(), CancellationToken.None);

    Assert.True(prefs.Enabled);
    Assert.Null(prefs.Email);
  }

  [Fact]
  public async Task SetAsync_RoundTripsAndUpserts()
  {
    var user = Guid.NewGuid();
    await _store.SetAsync(new UserNotificationPrefs { UserId = user, Enabled = true, Email = "a@example", NtfyTopic = "t1" }, CancellationToken.None);
    await _store.SetAsync(new UserNotificationPrefs { UserId = user, Enabled = false, Email = "b@example" }, CancellationToken.None);

    var prefs = await _store.GetAsync(user, CancellationToken.None);

    Assert.False(prefs.Enabled);
    Assert.Equal("b@example", prefs.Email);
    Assert.Null(prefs.NtfyTopic);
  }
}
