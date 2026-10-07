using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// Tests for <see cref="ServarrDownloadClient"/> on shows TMDB splits otherwise than TVDB: the request keeps
/// TMDB's numbering, Sonarr gets its own, through a mapping matched by air dates.
/// </summary>
public sealed class ServarrMappedShowTests : IDisposable
{
  private const string Url = "http://localhost:8989";
  private const string Key = "sk";
  private static readonly Func<CancellationToken, Task> NoDelay = _ => Task.CompletedTask;

  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonSeriesMappingStore _mappings;

  public ServarrMappedShowTests() => _mappings = new JsonSeriesMappingStore(_path);

  public void Dispose()
  {
    _mappings.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private static PluginConfiguration Config() => new()
  {
    DownloadBackend = "servarr",
    SonarrUrl = Url,
    SonarrApiKey = Key,
    SonarrRootFolderPath = "/tv",
    SonarrQualityProfileId = 1
  };

  private static string Episodes(params (int Season, int Episode, string Date, int Id)[] episodes)
    => new JsonArray(episodes.Select(e => (JsonNode)new JsonObject
    {
      ["id"] = e.Id,
      ["seasonNumber"] = e.Season,
      ["episodeNumber"] = e.Episode,
      ["airDate"] = e.Date,
      ["monitored"] = false,
      ["episodeFileId"] = e.Id * 10
    }).ToArray()).ToJsonString();

  // "Berlin (2023)": season 1 from 2023-12-29, season 2 (TMDB's "Berlin and the Lady with an Ermine") on 2026-05-15.
  private static string BerlinEpisodes() => Episodes(
    Enumerable.Range(1, 3).Select(n => (1, n, "2023-12-29", n))
      .Concat(Enumerable.Range(1, 3).Select(n => (2, n, "2026-05-15", 100 + n))).ToArray());

  private static JsonObject BerlinSeries() => new()
  {
    ["id"] = 7,
    ["title"] = "Berlin (2023)",
    ["tvdbId"] = 413033,
    ["monitored"] = true,
    ["seasons"] = new JsonArray(
      new JsonObject { ["seasonNumber"] = 1, ["monitored"] = false },
      new JsonObject { ["seasonNumber"] = 2, ["monitored"] = false })
  };

  private static Mock<ITmdbClient> BerlinOnTmdb()
  {
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(308014, It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
    tmdb.Setup(t => t.GetDetailsAsync("tv", 308014, It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new CatalogItem { TmdbId = 308014, MediaType = "tv", Title = "Berlin and the Lady with an Ermine", ImdbId = "tt42178219", ReleaseDate = "2026-05-15" });
    tmdb.Setup(t => t.GetSeasonsAsync(308014, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new Season { SeasonNumber = 1 } });
    tmdb.Setup(t => t.GetSeasonEpisodesAsync(308014, 1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(Enumerable.Range(1, 3).Select(n => new Episode { SeasonNumber = 1, EpisodeNumber = n, AirDate = "2026-05-15" }).ToList());
    tmdb.Setup(t => t.GetShowLinksAsync(308014, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new ShowLinks { TmdbId = 308014, Name = "Berlin and the Lady with an Ermine", OriginalName = "Berlín y la dama del armiño", CreatorIds = new[] { 1, 2 } });
    tmdb.Setup(t => t.SearchShowsAsync("Berlin", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new[] { new CatalogItem { TmdbId = 146176, MediaType = "tv", Title = "Berlin" }, new CatalogItem { TmdbId = 61889, MediaType = "tv", Title = "Babylon Berlin" } });
    tmdb.Setup(t => t.GetShowLinksAsync(146176, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new ShowLinks { TmdbId = 146176, Name = "Berlin", CreatorIds = new[] { 1 }, TvdbId = 413033 });
    tmdb.Setup(t => t.GetShowLinksAsync(61889, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new ShowLinks { TmdbId = 61889, Name = "Babylon Berlin", CreatorIds = new[] { 99 }, TvdbId = 327550 });
    return tmdb;
  }

  private ServarrDownloadClient Client(Mock<IServarrClient> servarr, Mock<ITmdbClient> tmdb)
    => new(servarr.Object, tmdb.Object, Config, NoDelay, mappings: _mappings);

  private static void VerifySeasonSearch(Mock<IServarrClient> servarr, int season)
    => servarr.Verify(s => s.CommandAsync(Url, Key, It.Is<JsonObject>(c => (string)c["name"]! == "SeasonSearch" && (int)c["seasonNumber"]! == season), It.IsAny<CancellationToken>()), Times.Once);

  [Fact]
  public async Task Berlin_AddedByHand_IsFoundThroughItsTmdbFamily_AndSeasonTwoIsRequested()
  {
    // The reported case: no TVDB id on TMDB, an IMDb id Sonarr does not know, no Sonarr link — but TMDB's
    // "Berlin" (same creators) is TVDB 413033, whose season 2 airs the same day as TMDB's season 1.
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetSeriesByTvdbAsync(Url, Key, 413033, It.IsAny<CancellationToken>())).ReturnsAsync(BerlinSeries);
    servarr.Setup(s => s.GetEpisodesAsync(Url, Key, 7, It.IsAny<CancellationToken>())).ReturnsAsync(BerlinEpisodes());
    var client = Client(servarr, BerlinOnTmdb());

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 308014, MediaType = "tv", Title = "Berlin and the Lady with an Ermine", Season = 1 }, CancellationToken.None);

    var mapping = _mappings.Get(308014);
    Assert.Equal(413033, mapping!.TvdbId);
    Assert.Equal(2, mapping.ToSonarr(1));
    VerifySeasonSearch(servarr, 2);
    servarr.Verify(s => s.UpdateSeriesAsync(Url, Key, 7, It.Is<JsonObject>(b => SeasonMonitored(b, 2) && !SeasonMonitored(b, 1)), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    servarr.Verify(s => s.SetEpisodesMonitoredAsync(Url, Key, It.Is<IReadOnlyList<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 101, 102, 103 })), true, It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.AddSeriesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task WholeShowRequest_OfAMappedShow_IsItsSeason_NotTheWholeSeries()
  {
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetSeriesByTvdbAsync(Url, Key, 413033, It.IsAny<CancellationToken>())).ReturnsAsync(BerlinSeries);
    servarr.Setup(s => s.GetEpisodesAsync(Url, Key, 7, It.IsAny<CancellationToken>())).ReturnsAsync(BerlinEpisodes());
    var client = Client(servarr, BerlinOnTmdb());

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 308014, MediaType = "tv", Title = "Berlin and the Lady with an Ermine" }, CancellationToken.None);

    VerifySeasonSearch(servarr, 2);
    servarr.Verify(s => s.CommandAsync(Url, Key, It.Is<JsonObject>(c => (string)c["name"]! == "SeriesSearch"), It.IsAny<CancellationToken>()), Times.Never);
    servarr.Verify(s => s.UpdateSeriesAsync(Url, Key, 7, It.Is<JsonObject>(b => SeasonMonitored(b, 1)), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task AKnownMapping_IsUsedStraightAway()
  {
    await _mappings.SetAsync(BerlinMapping(), CancellationToken.None);
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetSeriesByTvdbAsync(Url, Key, 413033, It.IsAny<CancellationToken>())).ReturnsAsync(BerlinSeries);
    servarr.Setup(s => s.GetEpisodesAsync(Url, Key, 7, It.IsAny<CancellationToken>())).ReturnsAsync(BerlinEpisodes());
    var tmdb = BerlinOnTmdb();
    var client = Client(servarr, tmdb);

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 308014, MediaType = "tv", Title = "Berlin and the Lady with an Ermine", Season = 1, Episode = 2 }, CancellationToken.None);

    servarr.Verify(s => s.CommandAsync(Url, Key, It.Is<JsonObject>(c => (string)c["name"]! == "EpisodeSearch" && (int)c["episodeIds"]![0]! == 102), It.IsAny<CancellationToken>()), Times.Once);
    tmdb.Verify(t => t.SearchShowsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task LizzieBorden_IsAddedInert_CheckedByAirDates_ThenSeasonFourIsRequested()
  {
    // Sonarr links TVDB's "Monster (2022)" to TMDB 299939, but it is not in Sonarr yet: it is added with
    // nothing monitored, its episodes compared, then only season 4 is monitored and searched.
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(299939, It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
    tmdb.Setup(t => t.GetDetailsAsync("tv", 299939, It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new CatalogItem { TmdbId = 299939, MediaType = "tv", Title = "Monster: The Lizzie Borden Story", ReleaseDate = "2026-09-17" });
    tmdb.Setup(t => t.GetSeasonEpisodesAsync(299939, 1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(Enumerable.Range(1, 2).Select(n => new Episode { SeasonNumber = 1, EpisodeNumber = n, AirDate = "2026-09-17" }).ToList());
    var lookup = new JsonObject { ["title"] = "Monster (2022)", ["tvdbId"] = 389492, ["firstAired"] = "2022-09-21T00:00:00Z", ["lastAired"] = "2026-09-17T00:00:00Z", ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 1 }, new JsonObject { ["seasonNumber"] = 4 }) };
    JsonObject Monster(bool pending) => new()
    {
      ["id"] = 50,
      ["title"] = "Monster (2022)",
      ["tvdbId"] = 389492,
      ["monitored"] = true,
      ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 1, ["monitored"] = false }, new JsonObject { ["seasonNumber"] = 4, ["monitored"] = false }),
      ["addOptions"] = pending ? new JsonObject { ["monitor"] = "none" } : null
    };
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.LookupSeriesByTmdbAsync(Url, Key, 299939, It.IsAny<CancellationToken>())).ReturnsAsync(lookup);
    servarr.Setup(s => s.LookupSeriesAsync(Url, Key, 389492, It.IsAny<CancellationToken>())).ReturnsAsync(lookup);
    var added = false;
    var reads = 0;
    servarr.Setup(s => s.AddSeriesAsync(Url, Key, It.IsAny<JsonObject>(), It.IsAny<CancellationToken>())).Callback(() => added = true).Returns(Task.CompletedTask);
    servarr.Setup(s => s.GetSeriesByTvdbAsync(Url, Key, 389492, It.IsAny<CancellationToken>()))
      .ReturnsAsync(() => !added ? null : Monster(pending: reads++ < 1));
    servarr.Setup(s => s.GetEpisodesAsync(Url, Key, 50, It.IsAny<CancellationToken>()))
      .ReturnsAsync(Episodes((1, 1, "2022-09-21", 1), (1, 2, "2022-09-21", 2), (4, 1, "2026-09-17", 41), (4, 2, "2026-09-17", 42)));
    var client = Client(servarr, tmdb);

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 299939, MediaType = "tv", Title = "Monster: The Lizzie Borden Story", Season = 1 }, CancellationToken.None);

    servarr.Verify(s => s.AddSeriesAsync(Url, Key, It.Is<JsonObject>(b => !SeasonMonitored(b, 1) && !SeasonMonitored(b, 4) && (string)b["monitorNewItems"]! == "none"), It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal(4, _mappings.Get(299939)!.ToSonarr(1));
    VerifySeasonSearch(servarr, 4);
    servarr.Verify(s => s.DeleteSeriesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task ACandidateWhoseEpisodesDoNotMatch_IsRemovedAgain_AndTheRequestRefused()
  {
    var tmdb = BerlinOnTmdb();
    var lookup = new JsonObject { ["title"] = "Berlin (2023)", ["tvdbId"] = 413033, ["firstAired"] = "2023-12-29T00:00:00Z", ["seasons"] = new JsonArray() };
    var servarr = new Mock<IServarrClient>();
    var added = false;
    servarr.Setup(s => s.LookupSeriesAsync(Url, Key, 413033, It.IsAny<CancellationToken>())).ReturnsAsync(lookup);
    servarr.Setup(s => s.AddSeriesAsync(Url, Key, It.IsAny<JsonObject>(), It.IsAny<CancellationToken>())).Callback(() => added = true).Returns(Task.CompletedTask);
    servarr.Setup(s => s.GetSeriesByTvdbAsync(Url, Key, 413033, It.IsAny<CancellationToken>())).ReturnsAsync(() => added ? BerlinSeries() : null);
    // Only season 1 listed: nothing airs on 2026-05-15.
    servarr.Setup(s => s.GetEpisodesAsync(Url, Key, 7, It.IsAny<CancellationToken>())).ReturnsAsync(Episodes((1, 1, "2023-12-29", 1)));
    var client = Client(servarr, tmdb);

    var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      client.DispatchAsync(new DownloadDispatch { TmdbId = 308014, MediaType = "tv", Title = "Berlin and the Lady with an Ermine", Season = 1 }, CancellationToken.None));

    Assert.Contains("by hand", error.Message, StringComparison.Ordinal);
    servarr.Verify(s => s.DeleteSeriesAsync(Url, Key, 7, false, It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.CommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Never);
    Assert.Null(_mappings.Get(308014));
  }

  [Fact]
  public async Task Purge_OfAMappedShow_TakesItsSeasonOnly()
  {
    await _mappings.SetAsync(BerlinMapping(), CancellationToken.None);
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetSeriesByTvdbAsync(Url, Key, 413033, It.IsAny<CancellationToken>())).ReturnsAsync(BerlinSeries);
    servarr.Setup(s => s.GetEpisodesAsync(Url, Key, 7, It.IsAny<CancellationToken>())).ReturnsAsync(BerlinEpisodes());
    servarr.Setup(s => s.GetQueueAsync(Url, Key, true, It.IsAny<CancellationToken>())).ReturnsAsync("{ \"records\": [] }");
    var client = Client(servarr, BerlinOnTmdb());

    var ok = await client.PurgeAsync(new DownloadDispatch { TmdbId = 308014, MediaType = "tv", Title = "Berlin and the Lady with an Ermine" }, CancellationToken.None);

    Assert.True(ok);
    // The whole-show request is season 2 of Berlin (2023): its files go, season 1 and the series stay.
    servarr.Verify(s => s.DeleteSeriesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    foreach (var file in new[] { 1010, 1020, 1030 })
    {
      servarr.Verify(s => s.DeleteEpisodeFileAsync(Url, Key, file, It.IsAny<CancellationToken>()), Times.Once);
    }

    foreach (var file in new[] { 10, 20, 30 })
    {
      servarr.Verify(s => s.DeleteEpisodeFileAsync(Url, Key, file, It.IsAny<CancellationToken>()), Times.Never);
    }
  }

  [Fact]
  public async Task Cancel_OfAMappedSeason_ReleasesThatSeasonOnly()
  {
    await _mappings.SetAsync(BerlinMapping(), CancellationToken.None);
    var series = BerlinSeries();
    ((JsonObject)((JsonArray)series["seasons"]!)[0]!)["monitored"] = true;
    ((JsonObject)((JsonArray)series["seasons"]!)[1]!)["monitored"] = true;
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetSeriesByTvdbAsync(Url, Key, 413033, It.IsAny<CancellationToken>())).ReturnsAsync(series);
    servarr.Setup(s => s.GetEpisodesAsync(Url, Key, 7, It.IsAny<CancellationToken>())).ReturnsAsync(BerlinEpisodes());
    var client = Client(servarr, BerlinOnTmdb());

    await client.CancelAsync(new DownloadDispatch { TmdbId = 308014, MediaType = "tv", Title = "Berlin and the Lady with an Ermine", Season = 1 }, CancellationToken.None);

    servarr.Verify(s => s.UpdateSeriesAsync(Url, Key, 7, It.Is<JsonObject>(b => !SeasonMonitored(b, 2) && SeasonMonitored(b, 1)), It.IsAny<CancellationToken>()), Times.Once);
  }

  private static SeriesMapping BerlinMapping()
  {
    var mapping = new SeriesMapping { TmdbId = 308014, TvdbId = 413033, TvdbTitle = "Berlin (2023)" };
    mapping.Seasons.Add(new SeasonLink { TmdbSeason = 1, SonarrSeason = 2 });
    return mapping;
  }

  private static bool SeasonMonitored(JsonObject series, int season)
    => series["seasons"] is JsonArray seasons
      && seasons.OfType<JsonObject>().Any(s => (int)s["seasonNumber"]! == season && s["monitored"] is JsonValue m && m.GetValue<bool>());
}
