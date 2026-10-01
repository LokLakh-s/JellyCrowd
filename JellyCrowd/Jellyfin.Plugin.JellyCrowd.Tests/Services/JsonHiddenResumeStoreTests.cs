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
/// Tests for <see cref="JsonHiddenResumeStore"/>.
/// </summary>
public sealed class JsonHiddenResumeStoreTests : IDisposable
{
  private static readonly Guid Viewer = Guid.NewGuid();
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonHiddenResumeStore _store;

  public JsonHiddenResumeStoreTests() => _store = new JsonHiddenResumeStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private static HiddenResumeEntry Movie(Guid id, int minutesAgo = 0)
    => new() { UserId = Viewer, ItemId = id, Title = "Movie", HiddenAtUtc = DateTime.UtcNow.AddMinutes(-minutesAgo) };

  private static HiddenResumeEntry Episode(Guid id, Guid series, int minutesAgo = 0)
    => new() { UserId = Viewer, ItemId = id, SeriesId = series, Title = "Show", HiddenAtUtc = DateTime.UtcNow.AddMinutes(-minutesAgo) };

  [Fact]
  public async Task Hide_ThenList_NewestFirst_PerUser()
  {
    var older = Guid.NewGuid();
    var newer = Guid.NewGuid();
    await _store.HideAsync(Movie(older, 10), CancellationToken.None);
    await _store.HideAsync(Movie(newer), CancellationToken.None);
    await _store.HideAsync(new HiddenResumeEntry { UserId = Guid.NewGuid(), ItemId = Guid.NewGuid() }, CancellationToken.None);

    var mine = await _store.GetByUserAsync(Viewer, CancellationToken.None);

    Assert.Equal(new[] { newer, older }, mine.Select(e => e.ItemId));
  }

  [Fact]
  public async Task Hide_AnotherEpisodeOfTheSameShow_ReplacesTheEntry()
  {
    var show = Guid.NewGuid();
    await _store.HideAsync(Episode(Guid.NewGuid(), show, 5), CancellationToken.None);
    var latest = Guid.NewGuid();
    await _store.HideAsync(Episode(latest, show), CancellationToken.None);

    var entry = Assert.Single(await _store.GetByUserAsync(Viewer, CancellationToken.None));
    Assert.Equal(latest, entry.ItemId);
  }

  [Fact]
  public async Task Unhide_ByTheShowOrByTheEpisode()
  {
    var show = Guid.NewGuid();
    var episode = Guid.NewGuid();
    await _store.HideAsync(Episode(episode, show), CancellationToken.None);
    Assert.True(await _store.UnhideAsync(Viewer, show, CancellationToken.None));

    await _store.HideAsync(Episode(episode, show), CancellationToken.None);
    Assert.True(await _store.UnhideAsync(Viewer, episode, CancellationToken.None));

    Assert.False(await _store.UnhideAsync(Viewer, episode, CancellationToken.None));
    Assert.Empty(await _store.GetByUserAsync(Viewer, CancellationToken.None));
  }

  [Fact]
  public async Task UnhideForPlayback_AnyEpisodeOfTheShow_PutsItBack()
  {
    var show = Guid.NewGuid();
    var movie = Guid.NewGuid();
    await _store.HideAsync(Episode(Guid.NewGuid(), show), CancellationToken.None);
    await _store.HideAsync(Movie(movie), CancellationToken.None);

    Assert.Equal(1, await _store.UnhideForPlaybackAsync(Viewer, Guid.NewGuid(), show, CancellationToken.None));
    Assert.Equal(0, await _store.UnhideForPlaybackAsync(Viewer, Guid.NewGuid(), null, CancellationToken.None));
    Assert.Equal(1, await _store.UnhideForPlaybackAsync(Viewer, movie, null, CancellationToken.None));
    Assert.Empty(await _store.GetByUserAsync(Viewer, CancellationToken.None));
  }

  [Fact]
  public async Task Removals_SurviveARestart()
  {
    var movie = Guid.NewGuid();
    await _store.HideAsync(Movie(movie), CancellationToken.None);

    using var reopened = new JsonHiddenResumeStore(_path);
    Assert.Equal(movie, Assert.Single(await reopened.GetByUserAsync(Viewer, CancellationToken.None)).ItemId);
  }

  [Fact]
  public async Task Hide_PastTheCap_DropsTheUsersOldest()
  {
    var first = Guid.NewGuid();
    await _store.HideAsync(Movie(first, JsonHiddenResumeStore.MaxPerUser + 10), CancellationToken.None);
    for (var i = 0; i < JsonHiddenResumeStore.MaxPerUser; i++)
    {
      await _store.HideAsync(Movie(Guid.NewGuid(), JsonHiddenResumeStore.MaxPerUser - i), CancellationToken.None);
    }

    var mine = await _store.GetByUserAsync(Viewer, CancellationToken.None);
    Assert.Equal(JsonHiddenResumeStore.MaxPerUser, mine.Count);
    Assert.DoesNotContain(mine, e => e.ItemId == first);
  }
}
