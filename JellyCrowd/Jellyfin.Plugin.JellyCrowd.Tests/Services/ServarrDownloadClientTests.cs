using System;
using System.Net.Http;
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
  public async Task DispatchAsync_Movie_AlreadyInRadarr_SearchesInsteadOfReadding()
  {
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetMovieByTmdbAsync("http://localhost:7878", "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["id"] = 5 });
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), RadarrConfig);

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 603, MediaType = "movie", Title = "The Matrix" }, CancellationToken.None);

    // Re-adding would 400 ("movie already exists") and cause the Blocked-flapping loop; instead we search.
    servarr.Verify(s => s.AddMovieAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Never);
    servarr.Verify(s => s.CommandAsync("http://localhost:7878", "rk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
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
  public async Task DispatchAsync_Show_AlreadyInSonarr_MonitorsRequestedSeasonThenSearches()
  {
    // Series exists from an earlier season; season 2 is present but UNMONITORED.
    var series = new JsonObject
    {
      ["id"] = 7,
      ["monitored"] = true,
      ["seasons"] = new JsonArray(
        new JsonObject { ["seasonNumber"] = 1, ["monitored"] = true },
        new JsonObject { ["seasonNumber"] = 2, ["monitored"] = false })
    };
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetSeriesByTvdbAsync("http://localhost:8989", "sk", 81189, It.IsAny<CancellationToken>()))
      .ReturnsAsync(series);
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>())).ReturnsAsync(81189);
    var client = new ServarrDownloadClient(servarr.Object, tmdb.Object, SonarrConfig);

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 1396, MediaType = "tv", Title = "BB", Season = 2 }, CancellationToken.None);

    // Season 2 must be monitored (persisted) before the search, otherwise nothing downloads.
    servarr.Verify(s => s.UpdateSeriesAsync("http://localhost:8989", "sk", 7, It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.CommandAsync("http://localhost:8989", "sk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.AddSeriesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Never);
    Assert.True(series["seasons"]![1]!["monitored"]!.GetValue<bool>());
  }

  [Fact]
  public async Task DispatchAsync_Show_AlreadyInSonarr_SeasonAlreadyMonitored_SkipsUpdate()
  {
    var series = new JsonObject
    {
      ["id"] = 7,
      ["monitored"] = true,
      ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 2, ["monitored"] = true })
    };
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetSeriesByTvdbAsync("http://localhost:8989", "sk", 81189, It.IsAny<CancellationToken>()))
      .ReturnsAsync(series);
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>())).ReturnsAsync(81189);
    var client = new ServarrDownloadClient(servarr.Object, tmdb.Object, SonarrConfig);

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 1396, MediaType = "tv", Title = "BB", Season = 2 }, CancellationToken.None);

    servarr.Verify(s => s.UpdateSeriesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Never);
    servarr.Verify(s => s.CommandAsync("http://localhost:8989", "sk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task DispatchAsync_Movie_AddRaces_RecoversBySearchingInsteadOfFailing()
  {
    // Two requests grabbed at once: the first add wins, so this add 400s ("movie already exists"). The
    // client must recover by searching the now-present movie rather than surfacing a spurious dispatch
    // error (which the UI would show as a transient "Blocked").
    var servarr = new Mock<IServarrClient>();
    servarr.SetupSequence(s => s.GetMovieByTmdbAsync("http://localhost:7878", "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync((JsonObject?)null)                // pre-check: not in Radarr yet
      .ReturnsAsync(new JsonObject { ["id"] = 9 });   // after the failed add: the race winner added it
    servarr.Setup(s => s.LookupMovieAsync("http://localhost:7878", "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["title"] = "The Matrix", ["tmdbId"] = 603 });
    servarr.Setup(s => s.AddMovieAsync("http://localhost:7878", "rk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new HttpRequestException("400 (Bad Request) — movie already exists"));
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), RadarrConfig);

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 603, MediaType = "movie", Title = "The Matrix" }, CancellationToken.None);

    servarr.Verify(s => s.CommandAsync("http://localhost:7878", "rk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task DispatchAsync_Movie_AddFailsAndStillAbsent_Rethrows()
  {
    // A genuine add failure (not a race): the movie is still absent afterwards, so the error must
    // propagate (retried/surfaced) rather than being silently swallowed.
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetMovieByTmdbAsync("http://localhost:7878", "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync((JsonObject?)null);
    servarr.Setup(s => s.LookupMovieAsync("http://localhost:7878", "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["title"] = "The Matrix", ["tmdbId"] = 603 });
    servarr.Setup(s => s.AddMovieAsync("http://localhost:7878", "rk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new HttpRequestException("500 (Server Error)"));
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), RadarrConfig);

    await Assert.ThrowsAsync<HttpRequestException>(() =>
      client.DispatchAsync(new DownloadDispatch { TmdbId = 603, MediaType = "movie", Title = "The Matrix" }, CancellationToken.None));
    servarr.Verify(s => s.CommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task DispatchAsync_Show_AddRaces_RecoversByMonitoringAndSearching()
  {
    // Two seasons of the same show grabbed at once: the first add wins, so this add 400s ("series already
    // added"). Recover by monitoring the requested season on the now-present series and searching it —
    // no spurious "Blocked".
    var added = new JsonObject
    {
      ["id"] = 7,
      ["monitored"] = true,
      ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 2, ["monitored"] = false })
    };
    var servarr = new Mock<IServarrClient>();
    servarr.SetupSequence(s => s.GetSeriesByTvdbAsync("http://localhost:8989", "sk", 81189, It.IsAny<CancellationToken>()))
      .ReturnsAsync((JsonObject?)null)     // pre-check: not in Sonarr yet
      .ReturnsAsync(added);                // after the failed add: the race winner added it
    servarr.Setup(s => s.LookupSeriesAsync("http://localhost:8989", "sk", 81189, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["title"] = "BB", ["tvdbId"] = 81189, ["seasons"] = new JsonArray() });
    servarr.Setup(s => s.AddSeriesAsync("http://localhost:8989", "sk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new HttpRequestException("400 (Bad Request) — series already added"));
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>())).ReturnsAsync(81189);
    var client = new ServarrDownloadClient(servarr.Object, tmdb.Object, SonarrConfig);

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 1396, MediaType = "tv", Title = "BB", Season = 2 }, CancellationToken.None);

    servarr.Verify(s => s.UpdateSeriesAsync("http://localhost:8989", "sk", 7, It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.CommandAsync("http://localhost:8989", "sk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
    Assert.True(added["seasons"]![0]!["monitored"]!.GetValue<bool>());
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
  public async Task CancelAsync_Movie_RemovesActiveDownloadFromQueue()
  {
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetQueueAsync("http://localhost:7878", "rk", false, It.IsAny<CancellationToken>()))
      .ReturnsAsync("{ \"records\": [ { \"id\": 42, \"movie\": { \"tmdbId\": 603 } } ] }");
    servarr.Setup(s => s.GetMovieByTmdbAsync("http://localhost:7878", "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["id"] = 5 });
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), RadarrConfig);

    await client.CancelAsync(new DownloadDispatch { TmdbId = 603, MediaType = "movie", Title = "The Matrix" }, CancellationToken.None);

    // The active grab must be removed from the download client (otherwise RDT keeps downloading)…
    servarr.Verify(s => s.DeleteQueueItemAsync("http://localhost:7878", "rk", 42, true, false, It.IsAny<CancellationToken>()), Times.Once);
    // …and the movie itself deleted from Radarr.
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
  public async Task PurgeAsync_WholeShow_DeletesEntireSeriesFromSonarr()
  {
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetQueueAsync("http://localhost:8989", "sk", true, It.IsAny<CancellationToken>()))
      .ReturnsAsync("{ \"records\": [] }");
    servarr.Setup(s => s.GetSeriesByTvdbAsync("http://localhost:8989", "sk", 81189, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["id"] = 7 });
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>())).ReturnsAsync(81189);
    var client = new ServarrDownloadClient(servarr.Object, tmdb.Object, SonarrConfig);

    // A whole-show request (no season) → remove the entire series.
    await client.PurgeAsync(new DownloadDispatch { TmdbId = 1396, MediaType = "tv", Title = "BB" }, CancellationToken.None);

    servarr.Verify(s => s.DeleteSeriesAsync("http://localhost:8989", "sk", 7, true, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task PurgeAsync_Season_UnmonitorsSeasonAndDeletesItsFiles_NotWholeSeries()
  {
    var series = new JsonObject
    {
      ["id"] = 7,
      ["monitored"] = true,
      ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 1, ["monitored"] = true })
    };
    var episodes = "[ { \"id\": 11, \"seasonNumber\": 1, \"episodeNumber\": 1, \"episodeFileId\": 101 },"
      + " { \"id\": 12, \"seasonNumber\": 1, \"episodeNumber\": 2, \"episodeFileId\": 102 },"
      + " { \"id\": 21, \"seasonNumber\": 2, \"episodeNumber\": 1, \"episodeFileId\": 201 } ]";
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetQueueAsync("http://localhost:8989", "sk", true, It.IsAny<CancellationToken>())).ReturnsAsync("{ \"records\": [] }");
    servarr.Setup(s => s.GetSeriesByTvdbAsync("http://localhost:8989", "sk", 81189, It.IsAny<CancellationToken>())).ReturnsAsync(series);
    servarr.Setup(s => s.GetEpisodesAsync("http://localhost:8989", "sk", 7, It.IsAny<CancellationToken>())).ReturnsAsync(episodes);
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>())).ReturnsAsync(81189);
    var client = new ServarrDownloadClient(servarr.Object, tmdb.Object, SonarrConfig);

    await client.PurgeAsync(new DownloadDispatch { TmdbId = 1396, MediaType = "tv", Title = "BB", Season = 1 }, CancellationToken.None);

    servarr.Verify(s => s.DeleteSeriesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    servarr.Verify(s => s.UpdateSeriesAsync("http://localhost:8989", "sk", 7, It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.DeleteEpisodeFileAsync("http://localhost:8989", "sk", 101, It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.DeleteEpisodeFileAsync("http://localhost:8989", "sk", 102, It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.DeleteEpisodeFileAsync("http://localhost:8989", "sk", 201, It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task PurgeAsync_Episode_DeletesOnlyThatEpisodeFile_AndUnmonitorsEpisode()
  {
    var series = new JsonObject { ["id"] = 7, ["seasons"] = new JsonArray() };
    var episodes = "[ { \"id\": 11, \"seasonNumber\": 1, \"episodeNumber\": 1, \"episodeFileId\": 101 },"
      + " { \"id\": 12, \"seasonNumber\": 1, \"episodeNumber\": 2, \"episodeFileId\": 102 } ]";
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetQueueAsync("http://localhost:8989", "sk", true, It.IsAny<CancellationToken>())).ReturnsAsync("{ \"records\": [] }");
    servarr.Setup(s => s.GetSeriesByTvdbAsync("http://localhost:8989", "sk", 81189, It.IsAny<CancellationToken>())).ReturnsAsync(series);
    servarr.Setup(s => s.GetEpisodesAsync("http://localhost:8989", "sk", 7, It.IsAny<CancellationToken>())).ReturnsAsync(episodes);
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>())).ReturnsAsync(81189);
    var client = new ServarrDownloadClient(servarr.Object, tmdb.Object, SonarrConfig);

    await client.PurgeAsync(new DownloadDispatch { TmdbId = 1396, MediaType = "tv", Title = "BB", Season = 1, Episode = 2 }, CancellationToken.None);

    servarr.Verify(s => s.DeleteEpisodeFileAsync("http://localhost:8989", "sk", 102, It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.DeleteEpisodeFileAsync("http://localhost:8989", "sk", 101, It.IsAny<CancellationToken>()), Times.Never);
    servarr.Verify(s => s.SetEpisodesMonitoredAsync("http://localhost:8989", "sk", It.Is<System.Collections.Generic.IReadOnlyList<int>>(l => l.Count == 1 && l[0] == 12), false, It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.DeleteSeriesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task CancelAsync_Show_StillDoesNotDeleteSeries()
  {
    // A user cancel of a single season must NOT nuke the whole series (only Purge does).
    var servarr = new Mock<IServarrClient>();
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), SonarrConfig);

    await client.CancelAsync(new DownloadDispatch { TmdbId = 1396, MediaType = "tv", Title = "BB", Season = 1 }, CancellationToken.None);

    servarr.Verify(s => s.DeleteSeriesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public void IsConfigured_TrueWhenEitherSideConfigured()
  {
    var client = new ServarrDownloadClient(Mock.Of<IServarrClient>(), Mock.Of<ITmdbClient>(), RadarrConfig);

    Assert.True(client.IsConfigured(RadarrConfig()));
    Assert.False(client.IsConfigured(new PluginConfiguration()));
  }
}
