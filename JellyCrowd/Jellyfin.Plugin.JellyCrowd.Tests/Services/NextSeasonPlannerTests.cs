using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="NextSeasonPlanner"/>.
/// </summary>
public class NextSeasonPlannerTests
{
  private static readonly DateTime Today = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

  private static List<Season> Show(params int?[] episodeCounts)
    => episodeCounts.Select((count, i) => new Season { SeasonNumber = i + 1, EpisodeCount = count }).ToList();

  [Theory]
  [InlineData(7, 2, null)] // 3 left: not yet
  [InlineData(8, 2, 2)]    // 2 left: request season 2
  [InlineData(10, 2, 2)]   // the finale
  [InlineData(10, 0, 2)]   // threshold 0: only the last episode
  [InlineData(9, 0, null)]
  public void NextSeasonToRequest_DependsOnEpisodesLeft(int episode, int threshold, int? expected)
  {
    Assert.Equal(expected, NextSeasonPlanner.NextSeasonToRequest(Show(10, 8), 1, episode, threshold));
  }

  [Fact]
  public void NextSeasonToRequest_NoNextSeason_IsNull()
  {
    Assert.Null(NextSeasonPlanner.NextSeasonToRequest(Show(10, 8), 2, 8, 2));
  }

  [Fact]
  public void NextSeasonToRequest_AnnouncedButEmptyNextSeason_IsNull()
  {
    Assert.Null(NextSeasonPlanner.NextSeasonToRequest(Show(10, 0), 1, 10, 2));
  }

  [Fact]
  public void NextSeasonToRequest_NextSeasonOfUnknownLength_IsRequested()
  {
    Assert.Equal(2, NextSeasonPlanner.NextSeasonToRequest(Show(10, null), 1, 10, 2));
  }

  [Fact]
  public void NextSeasonToRequest_CurrentSeasonOfUnknownLength_IsNull()
  {
    // Without the season's length there is no telling how close its end is.
    Assert.Null(NextSeasonPlanner.NextSeasonToRequest(Show(null, 8), 1, 10, 2));
  }

  [Theory]
  [InlineData(0, 3)] // a special
  [InlineData(1, 0)] // an unnumbered episode
  public void NextSeasonToRequest_SpecialsAndUnnumbered_AreIgnored(int season, int episode)
  {
    var seasons = new List<Season> { new() { SeasonNumber = 0, EpisodeCount = 3 }, new() { SeasonNumber = 1, EpisodeCount = 3 } };

    Assert.Null(NextSeasonPlanner.NextSeasonToRequest(seasons, season, episode, 2));
  }

  [Fact]
  public void NextSeasonToRequest_ThresholdIsClamped()
  {
    // A threshold far above the maximum behaves as the maximum (10): episode 1 of 12 leaves 11.
    Assert.Null(NextSeasonPlanner.NextSeasonToRequest(Show(12, 8), 1, 1, 99));
    Assert.Equal(2, NextSeasonPlanner.NextSeasonToRequest(Show(12, 8), 1, 2, 99));
  }

  [Fact]
  public void PlanRequests_AllAired_IsOneSeasonRequest()
  {
    var episodes = new List<Episode>
    {
      new() { SeasonNumber = 2, EpisodeNumber = 1, AirDate = "2026-01-01" },
      new() { SeasonNumber = 2, EpisodeNumber = 2, AirDate = "2026-01-08" },
    };

    var plan = NextSeasonPlanner.PlanRequests(episodes, Today);

    Assert.Equal(new (int?, string?)[] { (null, null) }, plan);
  }

  [Fact]
  public void PlanRequests_SomeStillToAir_IsOneRequestPerEpisode()
  {
    var episodes = new List<Episode>
    {
      new() { SeasonNumber = 2, EpisodeNumber = 0, AirDate = "2026-09-01" }, // unnumbered placeholder: skipped
      new() { SeasonNumber = 2, EpisodeNumber = 1, AirDate = "2026-09-24" },
      new() { SeasonNumber = 2, EpisodeNumber = 2, AirDate = "2026-10-08" },
    };

    var plan = NextSeasonPlanner.PlanRequests(episodes, Today);

    Assert.Equal(new (int?, string?)[] { (1, "2026-09-24"), (2, "2026-10-08") }, plan);
  }

  [Fact]
  public void PlanRequests_AiringToday_CountsAsAired()
  {
    var episodes = new List<Episode> { new() { SeasonNumber = 2, EpisodeNumber = 1, AirDate = "2026-10-01" } };

    Assert.Equal(new (int?, string?)[] { (null, null) }, NextSeasonPlanner.PlanRequests(episodes, Today));
  }

  [Fact]
  public void PlanRequests_NoEpisodesListed_IsOneSeasonRequest()
  {
    Assert.Equal(new (int?, string?)[] { (null, null) }, NextSeasonPlanner.PlanRequests(new List<Episode>(), Today));
  }
}
