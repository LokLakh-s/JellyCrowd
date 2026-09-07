using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestFootprint"/> — how many episodes a request covers, which is what the quota
/// gate reserves for it.
/// </summary>
public class RequestFootprintTests
{
  private static IReadOnlyList<Season> Seasons() => new List<Season>
  {
    new() { SeasonNumber = 0, EpisodeCount = 3 },   // specials
    new() { SeasonNumber = 1, EpisodeCount = 10 },
    new() { SeasonNumber = 2, EpisodeCount = 10 },
    new() { SeasonNumber = 3, EpisodeCount = 10 },
  };

  [Fact]
  public void EpisodesCovered_IsOne_ForAMovie()
    => Assert.Equal(1, RequestFootprint.EpisodesCovered("movie", null, null, Seasons()));

  [Fact]
  public void EpisodesCovered_IsOne_ForASingleEpisode()
    => Assert.Equal(1, RequestFootprint.EpisodesCovered("tv", 2, 5, Seasons()));

  [Fact]
  public void EpisodesCovered_IsTheSeasonCount_ForASeasonRequest()
    => Assert.Equal(10, RequestFootprint.EpisodesCovered("tv", 2, null, Seasons()));

  [Fact]
  public void EpisodesCovered_SumsTheRealSeasons_ForAWholeSeriesRequest()
  {
    // 3 seasons of 10 — the 3 specials are not fetched with a series, so they are not reserved.
    Assert.Equal(30, RequestFootprint.EpisodesCovered("tv", null, null, Seasons()));
  }

  [Fact]
  public void EpisodesCovered_IsTheSpecialsCount_WhenSeasonZeroIsAskedForExplicitly()
    => Assert.Equal(3, RequestFootprint.EpisodesCovered("tv", 0, null, Seasons()));

  [Theory]
  [InlineData(null)]
  [InlineData(1)]
  public void EpisodesCovered_FallsBackToOne_WhenTheSeasonListIsUnknown(int? season)
  {
    Assert.Equal(1, RequestFootprint.EpisodesCovered("tv", season, null, null));
    Assert.Equal(1, RequestFootprint.EpisodesCovered("tv", season, null, new List<Season>()));
  }

  [Fact]
  public void EpisodesCovered_FallsBackToOne_WhenTheProviderReportsNoCounts()
  {
    // Sonarr only reports counts for series it already tracks: unknown is not zero, and must not make a
    // whole-series request look free.
    var unknown = new List<Season> { new() { SeasonNumber = 1 }, new() { SeasonNumber = 2 } };

    Assert.Equal(1, RequestFootprint.EpisodesCovered("tv", null, null, unknown));
    Assert.Equal(1, RequestFootprint.EpisodesCovered("tv", 1, null, unknown));
  }

  [Fact]
  public void EpisodesCovered_FallsBackToOne_ForASeasonThatIsNotListed()
    => Assert.Equal(1, RequestFootprint.EpisodesCovered("tv", 9, null, Seasons()));
}
