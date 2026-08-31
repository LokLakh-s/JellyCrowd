using System.Text.Json.Nodes;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrPayload"/>.
/// </summary>
public class ServarrPayloadTests
{
  [Fact]
  public void BuildMovieAdd_SetsProfileRootAndSearchOption()
  {
    var lookup = new JsonObject { ["title"] = "The Matrix", ["tmdbId"] = 603, ["titleSlug"] = "the-matrix-603" };

    var body = ServarrPayload.BuildMovieAdd(lookup, 4, "/movies");

    Assert.Equal("The Matrix", body["title"]!.GetValue<string>());
    Assert.Equal(4, body["qualityProfileId"]!.GetValue<int>());
    Assert.Equal("/movies", body["rootFolderPath"]!.GetValue<string>());
    Assert.True(body["monitored"]!.GetValue<bool>());
    Assert.Equal("released", body["minimumAvailability"]!.GetValue<string>());
    Assert.True(body["addOptions"]!["searchForMovie"]!.GetValue<bool>());
  }

  [Fact]
  public void BuildSeriesAdd_WholeSeries_MonitorsAll()
  {
    var lookup = NewSeriesLookup();

    var body = ServarrPayload.BuildSeriesAdd(lookup, 5, 1, "/tv", null);

    Assert.Equal(5, body["qualityProfileId"]!.GetValue<int>());
    Assert.Equal(1, body["languageProfileId"]!.GetValue<int>());
    Assert.Equal("/tv", body["rootFolderPath"]!.GetValue<string>());
    Assert.True(body["seasonFolder"]!.GetValue<bool>());

    // The series is added inert whatever the scope; the caller then monitors the right seasons and
    // searches them. This keeps the add from ever grabbing more than was requested.
    Assert.Equal("none", body["addOptions"]!["monitor"]!.GetValue<string>());
    Assert.False(body["addOptions"]!["searchForMissingEpisodes"]!.GetValue<bool>());
  }

  [Fact]
  public void BuildSeriesAdd_SpecificSeason_MonitorsOnlyThatSeason()
  {
    var lookup = NewSeriesLookup();

    var body = ServarrPayload.BuildSeriesAdd(lookup, 5, 0, "/tv", 2);

    // Added inert: nothing monitored, no search. Sonarr's addOptions.monitor overrides the seasons array
    // (defaulting to "all"), so a single-season request must NOT let the add grab — the caller monitors
    // and searches only the requested season afterwards.
    Assert.Equal("none", body["addOptions"]!["monitor"]!.GetValue<string>());
    Assert.False(body["addOptions"]!["searchForMissingEpisodes"]!.GetValue<bool>());
    Assert.Null(body["languageProfileId"]); // not set when languageProfileId <= 0
  }

  [Fact]
  public void AreSeasonsMonitored_TrueOnlyWhenSeriesAndTargetSeasonMonitored()
  {
    JsonObject Series(bool root, bool s1, bool s2) => new()
    {
      ["monitored"] = root,
      ["seasons"] = new JsonArray(
        new JsonObject { ["seasonNumber"] = 0, ["monitored"] = false }, // specials ignored
        new JsonObject { ["seasonNumber"] = 1, ["monitored"] = s1 },
        new JsonObject { ["seasonNumber"] = 2, ["monitored"] = s2 })
    };

    // Whole-series check: series + every real season must be monitored.
    Assert.True(ServarrPayload.AreSeasonsMonitored(Series(true, true, true), null));
    Assert.False(ServarrPayload.AreSeasonsMonitored(Series(true, true, false), null));
    Assert.False(ServarrPayload.AreSeasonsMonitored(Series(false, true, true), null)); // series unmonitored

    // Single-season check: series + only that season.
    Assert.True(ServarrPayload.AreSeasonsMonitored(Series(true, true, false), 1));
    Assert.False(ServarrPayload.AreSeasonsMonitored(Series(true, false, true), 1));
  }

  [Fact]
  public void AreSeasonsMonitored_MatchesEnsureSeasonsMonitored_NoChange()
  {
    // When AreSeasonsMonitored is true, EnsureSeasonsMonitored should report nothing to change.
    var monitored = new JsonObject
    {
      ["monitored"] = true,
      ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 1, ["monitored"] = true })
    };
    Assert.True(ServarrPayload.AreSeasonsMonitored(monitored, 1));
    Assert.False(ServarrPayload.EnsureSeasonsMonitored(monitored, 1));
  }

  private static JsonObject NewSeriesLookup()
    => new()
    {
      ["title"] = "Show",
      ["tvdbId"] = 111,
      ["titleSlug"] = "show",
      ["seasons"] = new JsonArray
      {
        new JsonObject { ["seasonNumber"] = 1, ["monitored"] = false },
        new JsonObject { ["seasonNumber"] = 2, ["monitored"] = false }
      }
    };
}
