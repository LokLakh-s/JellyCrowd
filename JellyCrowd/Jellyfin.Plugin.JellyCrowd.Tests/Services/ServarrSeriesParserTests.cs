using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrSeriesParser"/>.
/// </summary>
public class ServarrSeriesParserTests
{
  // A Sonarr series Sonarr already tracks: seasons carry statistics, so episode counts are known.
  private const string AddedSeriesJson = """
  {
    "id": 12,
    "title": "JUJUTSU KAISEN",
    "tvdbId": 377543,
    "seasons": [
      { "seasonNumber": 0, "monitored": false, "statistics": { "totalEpisodeCount": 7 } },
      { "seasonNumber": 1, "monitored": true,  "statistics": { "totalEpisodeCount": 24 } },
      { "seasonNumber": 2, "monitored": false, "statistics": { "totalEpisodeCount": 23 } },
      { "seasonNumber": 3, "monitored": false, "statistics": { "totalEpisodeCount": 12 } }
    ]
  }
  """;

  // A lookup for a series Sonarr does not track yet: right season numbers, no counts, no usable id.
  private const string LookupSeriesJson = """
  {
    "id": 0,
    "title": "JUJUTSU KAISEN",
    "tvdbId": 377543,
    "seasons": [
      { "seasonNumber": 1, "monitored": true },
      { "seasonNumber": 2, "monitored": true }
    ]
  }
  """;

  private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

  [Fact]
  public void ParseSeasons_AddedSeries_ReadsNumbersAndCounts()
  {
    var seasons = ServarrSeriesParser.ParseSeasons(Obj(AddedSeriesJson));

    Assert.Equal(4, seasons.Count);
    Assert.Equal(new[] { 0, 1, 2, 3 }, seasons.Select(s => s.SeasonNumber));
    Assert.Equal(24, seasons.Single(s => s.SeasonNumber == 1).EpisodeCount);
    Assert.Equal(23, seasons.Single(s => s.SeasonNumber == 2).EpisodeCount);
    Assert.Equal(12, seasons.Single(s => s.SeasonNumber == 3).EpisodeCount);
  }

  [Fact]
  public void ParseSeasons_Lookup_KeepsNumbers_ButLeavesCountsUnknown()
  {
    var seasons = ServarrSeriesParser.ParseSeasons(Obj(LookupSeriesJson));

    // Unknown is null, NOT zero: a zero would wrongly read as "this season has no episodes".
    Assert.Equal(new[] { 1, 2 }, seasons.Select(s => s.SeasonNumber));
    Assert.All(seasons, s => Assert.Null(s.EpisodeCount));
  }

  [Fact]
  public void ParseSeriesId_OnlyForAnAddedSeries()
  {
    Assert.Equal(12, ServarrSeriesParser.ParseSeriesId(Obj(AddedSeriesJson)));
    Assert.Null(ServarrSeriesParser.ParseSeriesId(Obj(LookupSeriesJson)));
  }

  [Fact]
  public void ParseEpisodes_KeepsOnlyTheSeason_AndOrdersThem()
  {
    const string Json = """
    [
      { "seasonNumber": 2, "episodeNumber": 2, "title": "Hidden Inventory", "airDate": "2023-07-13" },
      { "seasonNumber": 1, "episodeNumber": 1, "title": "Ryomen Sukuna",    "airDate": "2020-10-03" },
      { "seasonNumber": 2, "episodeNumber": 1, "title": "Gojo's Past",      "airDate": "2023-07-06" }
    ]
    """;

    var episodes = ServarrSeriesParser.ParseEpisodes(Json, 2);

    Assert.Equal(2, episodes.Count);
    Assert.Equal(new[] { 1, 2 }, episodes.Select(e => e.EpisodeNumber));
    Assert.All(episodes, e => Assert.Equal(2, e.SeasonNumber));
    Assert.Equal("Gojo's Past", episodes[0].Name);
    Assert.Equal("2023-07-06", episodes[0].AirDate);
  }

  [Fact]
  public void ParseEpisodes_UnknownSeason_IsEmpty()
  {
    Assert.Empty(ServarrSeriesParser.ParseEpisodes("""[{ "seasonNumber": 1, "episodeNumber": 1 }]""", 9));
  }
}
