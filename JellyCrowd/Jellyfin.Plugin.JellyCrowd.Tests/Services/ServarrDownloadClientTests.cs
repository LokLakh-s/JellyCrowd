using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrDownloadClient"/>.
/// </summary>
public class ServarrDownloadClientTests
{
  private static PluginConfiguration RadarrConfig() => new()
  {
    RadarrUrl = "http://localhost:7878",
    RadarrApiKey = "rk",
    RadarrRootFolderPath = "/movies",
    RadarrQualityProfileId = 4
  };

  private static PluginConfiguration SonarrConfig() => new()
  {
    SonarrUrl = "http://localhost:8989",
    SonarrApiKey = "sk",
    SonarrRootFolderPath = "/tv",
    SonarrQualityProfileId = 5,
    SonarrLanguageProfileId = 1
  };

  [Fact]
  public async Task DispatchAsync_Movie_LooksUpAndAddsToRadarr()
  {
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.LookupMovieAsync("http://localhost:7878", "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["title"] = "The Matrix", ["tmdbId"] = 603 });
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), RadarrConfig);

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 603, MediaType = "movie", Title = "The Matrix" }, CancellationToken.None);

    servarr.Verify(s => s.AddMovieAsync("http://localhost:7878", "rk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task DispatchAsync_Show_ResolvesTvdbThenAddsToSonarr()
  {
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.LookupSeriesAsync("http://localhost:8989", "sk", 81189, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["title"] = "Breaking Bad", ["tvdbId"] = 81189, ["seasons"] = new JsonArray() });
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>())).ReturnsAsync(81189);
    var client = new ServarrDownloadClient(servarr.Object, tmdb.Object, SonarrConfig);

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 1396, MediaType = "tv", Title = "Breaking Bad", Season = 1 }, CancellationToken.None);

    servarr.Verify(s => s.AddSeriesAsync("http://localhost:8989", "sk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task DispatchAsync_Movie_RadarrNotConfigured_Throws()
  {
    var client = new ServarrDownloadClient(Mock.Of<IServarrClient>(), Mock.Of<ITmdbClient>(), () => new PluginConfiguration());

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
      client.DispatchAsync(new DownloadDispatch { TmdbId = 1, MediaType = "movie", Title = "X" }, CancellationToken.None));
  }

  [Fact]
  public async Task DispatchAsync_Show_NoTvdb_Throws()
  {
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
    var client = new ServarrDownloadClient(Mock.Of<IServarrClient>(), tmdb.Object, SonarrConfig);

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
      client.DispatchAsync(new DownloadDispatch { TmdbId = 5, MediaType = "tv", Title = "Y" }, CancellationToken.None));
  }

  [Fact]
  public async Task CancelAsync_Movie_DeletesFromRadarr()
  {
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetMovieByTmdbAsync("http://localhost:7878", "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["id"] = 5 });
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), RadarrConfig);

    await client.CancelAsync(new DownloadDispatch { TmdbId = 603, MediaType = "movie", Title = "The Matrix" }, CancellationToken.None);

    servarr.Verify(s => s.DeleteMovieAsync("http://localhost:7878", "rk", 5, true, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task CancelAsync_Show_DoesNotDelete()
  {
    var servarr = new Mock<IServarrClient>();
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), SonarrConfig);

    await client.CancelAsync(new DownloadDispatch { TmdbId = 1, MediaType = "tv", Title = "Y", Season = 1 }, CancellationToken.None);

    servarr.Verify(
      s => s.DeleteMovieAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
      Times.Never);
  }

  [Fact]
  public void IsConfigured_TrueWhenEitherSideConfigured()
  {
    var client = new ServarrDownloadClient(Mock.Of<IServarrClient>(), Mock.Of<ITmdbClient>(), RadarrConfig);

    Assert.True(client.IsConfigured(RadarrConfig()));
    Assert.False(client.IsConfigured(new PluginConfiguration()));
  }
}
