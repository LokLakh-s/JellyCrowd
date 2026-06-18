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
}
