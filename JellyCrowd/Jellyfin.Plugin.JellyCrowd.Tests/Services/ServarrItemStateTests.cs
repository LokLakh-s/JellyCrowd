using System;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrItemState"/>.
/// </summary>
public class ServarrItemStateTests
{
  private static readonly DateTime Now = new(2026, 6, 19, 0, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void Movie_Null_ReturnsNull() => Assert.Null(ServarrItemState.Movie(null));

  [Fact]
  public void Movie_HasFile_Completed()
    => Assert.Equal("completed", ServarrItemState.Movie(new JsonObject { ["hasFile"] = true, ["isAvailable"] = true }));

  [Fact]
  public void Movie_AvailableNoFile_Missing()
    => Assert.Equal("missing", ServarrItemState.Movie(new JsonObject { ["hasFile"] = false, ["isAvailable"] = true }));

  [Fact]
  public void Movie_NotAvailable_Unreleased()
    => Assert.Equal("unreleased", ServarrItemState.Movie(new JsonObject { ["hasFile"] = false, ["isAvailable"] = false }));

  [Fact]
  public void Episodes_SeasonAllHaveFiles_Completed()
  {
    var json = """
    [ { "seasonNumber": 1, "episodeNumber": 1, "hasFile": true, "monitored": true },
      { "seasonNumber": 1, "episodeNumber": 2, "hasFile": true, "monitored": true } ]
    """;

    Assert.Equal("completed", ServarrItemState.Episodes(json, 1, null, Now));
  }

  [Fact]
  public void Episodes_AiredMonitoredNoFile_Missing()
  {
    var json = """
    [ { "seasonNumber": 2, "episodeNumber": 1, "hasFile": false, "monitored": true, "airDateUtc": "2020-01-01T00:00:00Z" } ]
    """;

    Assert.Equal("missing", ServarrItemState.Episodes(json, 2, null, Now));
  }

  [Fact]
  public void Episodes_FutureAirNoFile_Unreleased()
  {
    var json = """
    [ { "seasonNumber": 2, "episodeNumber": 5, "hasFile": false, "monitored": true, "airDateUtc": "2999-01-01T00:00:00Z" } ]
    """;

    Assert.Equal("unreleased", ServarrItemState.Episodes(json, 2, null, Now));
  }

  [Fact]
  public void Episodes_SpecificEpisode_IgnoresOthers()
  {
    var json = """
    [ { "seasonNumber": 1, "episodeNumber": 1, "hasFile": true, "monitored": true, "airDateUtc": "2020-01-01T00:00:00Z" },
      { "seasonNumber": 1, "episodeNumber": 2, "hasFile": false, "monitored": true, "airDateUtc": "2020-01-08T00:00:00Z" } ]
    """;

    Assert.Equal("completed", ServarrItemState.Episodes(json, 1, 1, Now));
    Assert.Equal("missing", ServarrItemState.Episodes(json, 1, 2, Now));
  }

  [Fact]
  public void Episodes_NoMatch_ReturnsNull()
    => Assert.Null(ServarrItemState.Episodes("""[ { "seasonNumber": 1, "episodeNumber": 1, "hasFile": false } ]""", 9, null, Now));
}
