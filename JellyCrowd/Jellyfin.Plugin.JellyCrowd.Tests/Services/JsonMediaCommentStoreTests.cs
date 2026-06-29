using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonMediaCommentStore"/>.
/// </summary>
public sealed class JsonMediaCommentStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonMediaCommentStore _store;

  public JsonMediaCommentStoreTests() => _store = new JsonMediaCommentStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private static MediaComment New(int tmdbId, string text = "hi") => new()
  {
    MediaType = "movie",
    TmdbId = tmdbId,
    UserId = Guid.NewGuid(),
    UserName = "u",
    Text = text
  };

  [Fact]
  public async Task AddAsync_AssignsIdAndTimestamp()
  {
    var added = await _store.AddAsync(New(1), CancellationToken.None);

    Assert.NotEqual(Guid.Empty, added.Id);
    Assert.NotEqual(default, added.CreatedAt);
  }

  [Fact]
  public async Task GetForTitle_FiltersByTitleAndHidden()
  {
    await _store.AddAsync(New(1, "a"), CancellationToken.None);
    var b = await _store.AddAsync(New(1, "b"), CancellationToken.None);
    await _store.AddAsync(New(2, "other-title"), CancellationToken.None);
    await _store.SetHiddenAsync(b.Id, true, CancellationToken.None);

    var visible = await _store.GetForTitleAsync("movie", 1, includeHidden: false, CancellationToken.None);
    var all = await _store.GetForTitleAsync("movie", 1, includeHidden: true, CancellationToken.None);

    Assert.Single(visible);
    Assert.Equal("a", visible[0].Text);
    Assert.Equal(2, all.Count);
  }

  [Fact]
  public async Task DeleteAsync_RemovesComment()
  {
    var a = await _store.AddAsync(New(1), CancellationToken.None);

    Assert.True(await _store.DeleteAsync(a.Id, CancellationToken.None));
    Assert.Empty(await _store.GetForTitleAsync("movie", 1, includeHidden: true, CancellationToken.None));
  }

  [Fact]
  public async Task AddAsync_CapsPerTitle()
  {
    for (var i = 0; i < 205; i++)
    {
      await _store.AddAsync(New(1, "c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), CancellationToken.None);
    }

    Assert.Equal(200, (await _store.GetForTitleAsync("movie", 1, includeHidden: true, CancellationToken.None)).Count);
  }
}
