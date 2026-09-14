using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestCoverage"/> — what a season or series request adds for a user, and what it
/// must reserve.
/// </summary>
public class RequestCoverageTests
{
  private static readonly HashSet<EpisodeKey> None = new();

  // Seasons 1..3 of ten episodes each.
  private static HashSet<EpisodeKey> Series(params int[] seasons)
  {
    var keys = new HashSet<EpisodeKey>();
    foreach (var season in seasons.Length == 0 ? new[] { 1, 2, 3 } : seasons)
    {
      foreach (var episode in Enumerable.Range(1, 10))
      {
        keys.Add(new EpisodeKey(season, episode));
      }
    }

    return keys;
  }

  [Fact]
  public void AFreshRequest_ReservesEveryEpisode()
  {
    var decision = RequestCoverage.Evaluate(Series(), None, None, None);

    Assert.False(decision.AlreadyCovered);
    Assert.Equal(30, decision.EpisodesToReserve);
  }

  [Fact]
  public void CompletingAPartlyOwnedSeries_ReservesOnlyWhatIsMissing()
  {
    // Season 1 owned and on disk: asking for the whole series is allowed and costs seasons 2 and 3.
    var season1 = Series(1);

    var decision = RequestCoverage.Evaluate(Series(), present: season1, coveredInFlight: None, coveredOwned: season1);

    Assert.False(decision.AlreadyCovered);
    Assert.Equal(20, decision.EpisodesToReserve);
  }

  [Fact]
  public void CompletingAFulfilledSeason_ThatIsMissingAnEpisode_IsAllowed()
  {
    // A season owned but delivered without episode 7: that episode can still be asked for.
    var season = Series(1);
    var onDisk = season.Where(k => k.Episode != 7).ToHashSet();

    var decision = RequestCoverage.Evaluate(season, onDisk, None, coveredOwned: season);

    Assert.False(decision.AlreadyCovered);
    Assert.Equal(1, decision.EpisodesToReserve);
  }

  [Fact]
  public void WhatIsAlreadyOnItsWay_IsCovered_AndReservesNothing()
  {
    var decision = RequestCoverage.Evaluate(Series(2), None, coveredInFlight: Series(), coveredOwned: None);

    Assert.True(decision.AlreadyCovered);
    Assert.Equal(0, decision.EpisodesToReserve);
  }

  [Fact]
  public void WhatIsOwnedAndOnDisk_IsCovered()
  {
    var season = Series(1);

    Assert.True(RequestCoverage.Evaluate(season, season, None, coveredOwned: season).AlreadyCovered);
  }

  [Fact]
  public void MediaSomeoneElseOwns_IsNotCovered_ButCostsNothingToDownload()
  {
    // Already in the library through another user: this user may still take ownership of it.
    var season = Series(1);

    var decision = RequestCoverage.Evaluate(season, present: season, coveredInFlight: None, coveredOwned: None);

    Assert.False(decision.AlreadyCovered);
    Assert.Equal(0, decision.EpisodesToReserve);
  }

  [Fact]
  public void AnEmptyRequest_IsNeverCovered()
  {
    var decision = RequestCoverage.Evaluate(None, None, None, None);

    Assert.False(decision.AlreadyCovered);
    Assert.Equal(0, decision.EpisodesToReserve);
  }
}
