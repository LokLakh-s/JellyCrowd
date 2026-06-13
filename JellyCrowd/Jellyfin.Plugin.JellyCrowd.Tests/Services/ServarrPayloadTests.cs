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
    Assert.Equal("all", body["addOptions"]!["monitor"]!.GetValue<string>());
    Assert.True(body["addOptions"]!["searchForMissingEpisodes"]!.GetValue<bool>());
  }

  [Fact]
  public void BuildSeriesAdd_SpecificSeason_MonitorsOnlyThatSeason()
  {
    var lookup = NewSeriesLookup();

    var body = ServarrPayload.BuildSeriesAdd(lookup, 5, 0, "/tv", 2);

    var seasons = (JsonArray)body["seasons"]!;
    foreach (var node in seasons)
    {
      var obj = (JsonObject)node!;
      var expected = obj["seasonNumber"]!.GetValue<int>() == 2;
      Assert.Equal(expected, obj["monitored"]!.GetValue<bool>());
    }

    Assert.Equal("none", body["addOptions"]!["monitor"]!.GetValue<string>());
    Assert.Null(body["languageProfileId"]); // not set when languageProfileId <= 0
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
