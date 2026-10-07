using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonSeriesMappingStore"/>.
/// </summary>
public sealed class JsonSeriesMappingStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");

  public void Dispose()
  {
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private static SeriesMapping Berlin()
  {
    var mapping = new SeriesMapping { TmdbId = 308014, TvdbId = 413033, TvdbTitle = "Berlin (2023)" };
    mapping.Seasons.Add(new SeasonLink { TmdbSeason = 1, SonarrSeason = 2 });
    return mapping;
  }

  [Fact]
  public async Task SetAsync_IsReadBackInMemory_AndAfterARestart()
  {
    using (var store = new JsonSeriesMappingStore(_path))
    {
      await store.SetAsync(Berlin(), CancellationToken.None);
      Assert.Equal(2, store.Get(308014)!.ToSonarr(1));
      Assert.Single(store.ForTvdb(413033));
    }

    using var reopened = new JsonSeriesMappingStore(_path);
    Assert.Null(reopened.Get(308014)); // nothing until loaded at startup
    await reopened.LoadAsync(CancellationToken.None);

    var loaded = reopened.Get(308014);
    Assert.Equal(413033, loaded!.TvdbId);
    Assert.Equal(1, loaded.ToTmdb(2));
    Assert.Null(loaded.ToSonarr(2));
  }

  [Fact]
  public async Task RemoveAsync_ForgetsTheShow()
  {
    using var store = new JsonSeriesMappingStore(_path);
    await store.SetAsync(Berlin(), CancellationToken.None);

    await store.RemoveAsync(308014, CancellationToken.None);

    Assert.Null(store.Get(308014));
    Assert.Empty(store.ForTvdb(413033));
  }

  [Fact]
  public void ToSonarrScope_AWholeShowMappedOntoOneSeason_IsThatSeason()
  {
    Assert.Equal(2, Berlin().ToSonarrScope(null));
    Assert.Equal(2, Berlin().ToSonarrScope(1));
    Assert.Null(Berlin().ToSonarrScope(2));
  }
}
