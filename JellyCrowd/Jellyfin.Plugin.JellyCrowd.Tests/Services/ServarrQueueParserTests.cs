using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrQueueParser"/>.
/// </summary>
public class ServarrQueueParserTests
{
  [Fact]
  public void ParseMovieQueue_MapsByTmdbWithPercentAndState()
  {
    var json = """
    { "records": [
      { "size": 1000, "sizeleft": 250, "status": "downloading", "trackedDownloadState": "downloading",
        "trackedDownloadStatus": "ok", "timeleft": "00:10:00", "movie": { "tmdbId": 603 } }
    ] }
    """;

    var map = ServarrQueueParser.ParseMovieQueue(json);

    Assert.True(map.ContainsKey(603));
    Assert.Equal("downloading", map[603].State);
    Assert.Equal(75, map[603].Percent);
    Assert.Equal("00:10:00", map[603].TimeLeft);
  }

  [Fact]
  public void ParseMovieQueue_AcceptsBareArrayAndMapsImportingState()
  {
    var json = """
    [ { "size": 100, "sizeleft": 0, "status": "completed", "trackedDownloadState": "importPending",
        "movie": { "tmdbId": 1 } } ]
    """;

    var map = ServarrQueueParser.ParseMovieQueue(json);

    Assert.Equal("importing", map[1].State);
    Assert.Equal(100, map[1].Percent);
  }

  [Fact]
  public void ParseMovieQueue_WarningStatusWins()
  {
    var json = """
    { "records": [ { "size": 100, "sizeleft": 50, "status": "downloading",
      "trackedDownloadStatus": "warning", "movie": { "tmdbId": 7 } } ] }
    """;

    Assert.Equal("warning", ServarrQueueParser.ParseMovieQueue(json)[7].State);
  }

  [Fact]
  public void ParseSeriesQueue_CarriesTvdbSeasonEpisode()
  {
    var json = """
    { "records": [
      { "size": 800, "sizeleft": 400, "status": "downloading", "trackedDownloadState": "downloading",
        "series": { "tvdbId": 81189 }, "episode": { "seasonNumber": 2, "episodeNumber": 5 } }
    ] }
    """;

    var items = ServarrQueueParser.ParseSeriesQueue(json);

    Assert.Single(items);
    Assert.Equal(81189, items[0].TvdbId);
    Assert.Equal(2, items[0].Season);
    Assert.Equal(5, items[0].Episode);
    Assert.Equal(50, items[0].Progress.Percent);
  }

  [Fact]
  public void ParseSeriesQueue_SkipsRecordsWithoutTvdbId()
  {
    var json = """{ "records": [ { "status": "downloading", "episode": { "seasonNumber": 1 } } ] }""";

    Assert.Empty(ServarrQueueParser.ParseSeriesQueue(json));
  }

  private const string MixedQueue = """
  { "records": [
    { "id": 1, "downloadId": "A", "series": { "tvdbId": 9 }, "episode": { "seasonNumber": 1, "episodeNumber": 2 } },
    { "id": 2, "downloadId": "B", "series": { "tvdbId": 9 }, "episode": { "seasonNumber": 1, "episodeNumber": 5 } },
    { "id": 3, "downloadId": "C", "series": { "tvdbId": 9 }, "episode": { "seasonNumber": 1, "episodeNumber": 1 } },
    { "id": 4, "downloadId": "C", "series": { "tvdbId": 9 }, "episode": { "seasonNumber": 2, "episodeNumber": 1 } },
    { "id": 5, "downloadId": "D", "series": { "tvdbId": 9 }, "episode": { "seasonNumber": 2, "episodeNumber": 3 } },
    { "id": 6, "downloadId": "D", "series": { "tvdbId": 9 }, "episode": { "seasonNumber": 2, "episodeNumber": 4 } },
    { "id": 7, "downloadId": "E", "series": { "tvdbId": 8 }, "episode": { "seasonNumber": 1, "episodeNumber": 2 } }
  ] }
  """;

  [Fact]
  public void ParseSeriesQueueRecordIds_NarrowsToTheSeasonOrTheEpisode()
  {
    Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, ServarrQueueParser.ParseSeriesQueueRecordIds(MixedQueue, 9));
    Assert.Equal(new[] { 1, 2, 3 }, ServarrQueueParser.ParseSeriesQueueRecordIds(MixedQueue, 9, 1));
    Assert.Equal(new[] { 1 }, ServarrQueueParser.ParseSeriesQueueRecordIds(MixedQueue, 9, 1, 2));
  }

  [Fact]
  public void ParseSeriesDownloadsWithin_KeepsDownloadsThatAlsoCarryOtherEpisodes()
  {
    // Download C spans seasons 1 and 2: purging either season alone must leave it running.
    Assert.Equal(new[] { 1, 2 }, ServarrQueueParser.ParseSeriesDownloadsWithin(MixedQueue, 9, 1, null));
    Assert.Equal(new[] { 5 }, ServarrQueueParser.ParseSeriesDownloadsWithin(MixedQueue, 9, 2, null)); // one id for pack D
    Assert.Equal(new[] { 1 }, ServarrQueueParser.ParseSeriesDownloadsWithin(MixedQueue, 9, 1, 2));
    Assert.Empty(ServarrQueueParser.ParseSeriesDownloadsWithin(MixedQueue, 9, 2, 3)); // E3 rides with E4 in pack D
  }

  [Fact]
  public void ParseSeriesDownloadsWithin_TheWholeSeries_TakesEveryDownloadOnce()
  {
    Assert.Equal(new[] { 1, 2, 3, 5 }, ServarrQueueParser.ParseSeriesDownloadsWithin(MixedQueue, 9, null, null));
  }

  [Fact]
  public void ParseSeriesDownloadsWithin_AnEpisodeSet_TakesOnlyDownloadsMadeOfThoseEpisodes()
  {
    var released = new System.Collections.Generic.HashSet<Jellyfin.Plugin.JellyCrowd.Models.EpisodeKey> { new(1, 2), new(2, 3) };

    // A (S1E2) qualifies; D carries S2E3 and the kept S2E4, so it stays.
    Assert.Equal(new[] { 1 }, ServarrQueueParser.ParseSeriesDownloadsWithin(MixedQueue, 9, released));
  }
}
