using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="SonarrReleasePlan"/>: what withdrawing a request takes from Sonarr, and what it must
/// leave to the title's other requests.
/// </summary>
public class SonarrReleasePlanTests
{
  // Season 1: E1-E3 monitored (E2 and E3 share one file); season 2: E1 monitored with a file; specials off.
  private static readonly IReadOnlyList<SonarrEpisode> Episodes = new[]
  {
    new SonarrEpisode(10, 0, 1, false, 0),
    new SonarrEpisode(11, 1, 1, true, 101),
    new SonarrEpisode(12, 1, 2, true, 102),
    new SonarrEpisode(13, 1, 3, true, 102),
    new SonarrEpisode(21, 2, 1, true, 201)
  };

  private static RequestScope Keep(int? season, int? episode) => new("tv", 1, season, episode);

  [Fact]
  public void Episode_NobodyElse_ReleasesIt_AndTurnsItsSeasonOff()
  {
    // A season left monitored for nobody would keep fetching episodes nobody owns. The cascade unmonitors
    // the whole season, so nothing is unmonitored or monitored again episode by episode.
    var plan = SonarrReleasePlan.Build(1, 1, Array.Empty<RequestScope>(), new[] { 1, 2 }, Episodes);

    Assert.Equal(new[] { 1 }, plan.SeasonsToTurnOff);
    Assert.Empty(plan.EpisodesToUnmonitor);
    Assert.Empty(plan.EpisodesToRemonitor);
    Assert.Equal(new[] { 101 }, plan.FilesToDelete);
    Assert.Equal(new[] { new EpisodeKey(1, 1) }, plan.Released);
    Assert.False(plan.StopFollowingNewSeasons);
  }

  [Fact]
  public void Episode_WithAnotherEpisodeOfTheSeasonWanted_TurnsTheSeasonOff_AndRestoresOnlyThatEpisode()
  {
    // E2 is someone else's: monitored again after the cascade. E3, monitored for nobody, stays off.
    var plan = SonarrReleasePlan.Build(1, 1, new[] { Keep(1, 2) }, new[] { 1, 2 }, Episodes);

    Assert.Equal(new[] { 1 }, plan.SeasonsToTurnOff);
    Assert.Equal(new[] { 12 }, plan.EpisodesToRemonitor);
    Assert.Equal(new[] { 101 }, plan.FilesToDelete);
  }

  [Fact]
  public void Episode_InASeasonSomeoneHolds_LeavesTheSeasonOn()
  {
    var plan = SonarrReleasePlan.Build(2, 1, new[] { Keep(1, null) }, new[] { 1, 2 }, Episodes);

    Assert.Equal(new[] { 2 }, plan.SeasonsToTurnOff);
    Assert.DoesNotContain(1, plan.SeasonsToTurnOff);
  }

  [Fact]
  public void Episode_OfASeasonNotMonitored_TurnsNothingOff_AndUnmonitorsIt()
  {
    var plan = SonarrReleasePlan.Build(1, 1, Array.Empty<RequestScope>(), new[] { 2 }, Episodes);

    Assert.Empty(plan.SeasonsToTurnOff);
    Assert.Equal(new[] { 11 }, plan.EpisodesToUnmonitor);
  }

  [Fact]
  public void Episode_WantedByASeasonRequest_IsKept()
  {
    var plan = SonarrReleasePlan.Build(1, 1, new[] { Keep(1, null) }, new[] { 1 }, Episodes);

    Assert.Empty(plan.SeasonsToTurnOff);
    Assert.Empty(plan.Released);
    Assert.Empty(plan.EpisodesToUnmonitor);
    Assert.Empty(plan.FilesToDelete);
  }

  [Fact]
  public void Season_WithAnotherUsersEpisode_TurnsTheSeasonOff_AndRestoresThatEpisodeAfterTheCascade()
  {
    // Turning season 1 off makes Sonarr unmonitor all of it, E2 included: E2 must be monitored again.
    var plan = SonarrReleasePlan.Build(1, null, new[] { Keep(1, 2) }, new[] { 1, 2 }, Episodes);

    Assert.Equal(new[] { 1 }, plan.SeasonsToTurnOff);
    Assert.Equal(new[] { 12 }, plan.EpisodesToRemonitor);
    Assert.Empty(plan.EpisodesToUnmonitor); // the cascade unmonitors E1 and E3
    Assert.Equal(new[] { new EpisodeKey(1, 1), new EpisodeKey(1, 3) }, plan.Released.OrderBy(k => k.Episode));
    // E3's file also holds the kept E2: only E1's file goes.
    Assert.Equal(new[] { 101 }, plan.FilesToDelete);
  }

  [Fact]
  public void Season_HeldByAnotherSeasonRequest_KeepsItsFlag()
  {
    var plan = SonarrReleasePlan.Build(1, null, new[] { Keep(1, null) }, new[] { 1 }, Episodes);

    Assert.Empty(plan.SeasonsToTurnOff);
    Assert.Empty(plan.Released);
  }

  [Fact]
  public void Season_AlreadyOff_IsNotTurnedOffAgain()
  {
    var plan = SonarrReleasePlan.Build(1, null, Array.Empty<RequestScope>(), new[] { 2 }, Episodes);

    Assert.Empty(plan.SeasonsToTurnOff);
    Assert.Equal(new[] { 11, 12, 13 }, plan.EpisodesToUnmonitor);
  }

  [Fact]
  public void Season_RestoresOnlyKeptEpisodesThatWereMonitored_SoAPendingRequestIsNotStartedEarly()
  {
    var episodes = new[] { new SonarrEpisode(11, 1, 1, true, 0), new SonarrEpisode(12, 1, 2, false, 0) };

    var plan = SonarrReleasePlan.Build(1, null, new[] { Keep(1, 2) }, new[] { 1 }, episodes);

    Assert.Empty(plan.EpisodesToRemonitor);
  }

  [Fact]
  public void WholeSeries_WithAnotherSeasonRequest_ReleasesTheRest_AndStopsFollowingNewSeasons()
  {
    var plan = SonarrReleasePlan.Build(null, null, new[] { Keep(2, null) }, new[] { 1, 2 }, Episodes);

    Assert.Equal(new[] { 1 }, plan.SeasonsToTurnOff);
    Assert.Empty(plan.EpisodesToRemonitor);
    Assert.DoesNotContain(new EpisodeKey(2, 1), plan.Released);
    Assert.DoesNotContain(new EpisodeKey(0, 1), plan.Released); // a whole-series request never covered specials
    Assert.True(plan.StopFollowingNewSeasons);
  }

  [Fact]
  public void WholeSeries_WithAnotherWholeSeriesRequest_KeepsEverything()
  {
    var plan = SonarrReleasePlan.Build(null, null, new[] { Keep(null, null) }, new[] { 1, 2 }, Episodes);

    Assert.Empty(plan.SeasonsToTurnOff);
    Assert.Empty(plan.Released);
    Assert.False(plan.StopFollowingNewSeasons);
  }
}
