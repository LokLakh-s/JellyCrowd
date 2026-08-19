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
/// Tests for <see cref="HistoryController"/> over real JSON stores.
/// </summary>
public sealed class HistoryControllerTests : IDisposable
{
  private static readonly Guid User = Guid.NewGuid();
  private static readonly Guid Other = Guid.NewGuid();
  private readonly string _histPath = Path.Combine(Path.GetTempPath(), "jc-h-" + Guid.NewGuid() + ".json");
  private readonly string _prefsPath = Path.Combine(Path.GetTempPath(), "jc-p-" + Guid.NewGuid() + ".json");
  private readonly JsonPlaybackHistoryStore _history;
  private readonly JsonUserPrefsStore _prefs;

  public HistoryControllerTests()
  {
    _history = new JsonPlaybackHistoryStore(_histPath);
    _prefs = new JsonUserPrefsStore(_prefsPath);
  }

  public void Dispose()
  {
    _history.Dispose();
    _prefs.Dispose();
    foreach (var p in new[] { _histPath, _prefsPath })
    {
      if (File.Exists(p)) { File.Delete(p); }
    }
  }

  private HistoryController Controller() => new(_history, _prefs, new FakeUserAccessor())
  {
    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
  };

  private async Task<Guid> SeedAsync(Guid user, string title)
  {
    await _history.AddAsync(
      new PlaybackRecord { UserId = user, ItemName = title, ItemType = "Movie", PlayedAtUtc = DateTime.UtcNow, Minutes = 10 },
      CancellationToken.None);
    var mine = await _history.GetByUserAsync(user, 0, CancellationToken.None);
    return mine[0].Id;
  }

  private static MyHistoryDto Dto(ActionResult<MyHistoryDto> result)
    => Assert.IsType<MyHistoryDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

  [Fact]
  public async Task Mine_ReturnsTheCallersOwnHistoryOnly()
  {
    await SeedAsync(User, "Mine");
    await SeedAsync(Other, "Theirs");

    var dto = Dto(await Controller().Mine(CancellationToken.None));

    Assert.False(dto.Hidden);
    Assert.Single(dto.Entries);
    Assert.Equal("Mine", dto.Entries[0].Title);
  }

  [Fact]
  public async Task Mine_WhenHidden_ReturnsHiddenAndNoEntries()
  {
    await SeedAsync(User, "Mine");
    await _prefs.SetAsync(new UserNotificationPrefs { UserId = User, HistoryHidden = true }, CancellationToken.None);

    var dto = Dto(await Controller().Mine(CancellationToken.None));

    Assert.True(dto.Hidden);
    Assert.Empty(dto.Entries); // recording continues, but the personal view is off
  }

  [Fact]
  public async Task SetHidden_TogglesTheFlag_WithoutTouchingRecords()
  {
    await SeedAsync(User, "Mine");

    Assert.IsType<NoContentResult>(await Controller().SetHidden(true, CancellationToken.None));
    Assert.True((await _prefs.GetAsync(User, CancellationToken.None)).HistoryHidden);
    // The records are still there — hiding is not erasing.
    Assert.Single(await _history.GetByUserAsync(User, 0, CancellationToken.None));

    Assert.IsType<NoContentResult>(await Controller().SetHidden(false, CancellationToken.None));
    Assert.False((await _prefs.GetAsync(User, CancellationToken.None)).HistoryHidden);
  }

  [Fact]
  public async Task Clear_ErasesOnlyTheCallersHistory()
  {
    await SeedAsync(User, "Mine1");
    await SeedAsync(User, "Mine2");
    await SeedAsync(Other, "Theirs");

    var result = await Controller().Clear(CancellationToken.None);

    Assert.Equal(2, Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Empty(await _history.GetByUserAsync(User, 0, CancellationToken.None));
    Assert.Single(await _history.GetByUserAsync(Other, 0, CancellationToken.None));
  }

  [Fact]
  public async Task DeleteOne_RemovesOwnEntry()
  {
    var id = await SeedAsync(User, "Mine");

    Assert.IsType<NoContentResult>(await Controller().DeleteOne(id, CancellationToken.None));
    Assert.Empty(await _history.GetByUserAsync(User, 0, CancellationToken.None));
  }

  [Fact]
  public async Task DeleteOne_OfSomeoneElsesEntry_Is404()
  {
    var othersId = await SeedAsync(Other, "Theirs");

    Assert.IsType<NotFoundResult>(await Controller().DeleteOne(othersId, CancellationToken.None));
    Assert.Single(await _history.GetByUserAsync(Other, 0, CancellationToken.None)); // untouched
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(false);
  }
}
