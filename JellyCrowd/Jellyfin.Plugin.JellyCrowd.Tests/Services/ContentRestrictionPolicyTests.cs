using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ContentRestrictionPolicy"/>.
/// </summary>
public class ContentRestrictionPolicyTests
{
  [Fact]
  public void Combine_NoParentalControlNorChildGroup_IsUnrestricted()
  {
    var r = ContentRestrictionPolicy.Combine(null, null, false, false, null);

    Assert.False(r.IsRestricted);
    Assert.False(r.IsChild);
  }

  [Fact]
  public void Combine_JellyfinLimitOnly_KeepsScoreSubScoreAndUnratedFlags()
  {
    var r = ContentRestrictionPolicy.Combine(13, 1, true, false, null);

    Assert.True(r.IsRestricted);
    Assert.Equal(13, r.MaxScore);
    Assert.Equal(1, r.MaxSubScore);
    Assert.True(r.BlockUnratedMovies);
    Assert.False(r.BlockUnratedShows);
    Assert.False(r.IsChild);
  }

  [Fact]
  public void Combine_ChildGroupStricter_WinsAndBlocksUnrated()
  {
    // Jellyfin allows up to 16, the child group only up to 10: the lower age limit wins, with no sub-score.
    var r = ContentRestrictionPolicy.Combine(16, 2, false, false, 10);

    Assert.Equal(10, r.MaxScore);
    Assert.Null(r.MaxSubScore);
    Assert.True(r.BlockUnratedMovies);
    Assert.True(r.BlockUnratedShows);
    Assert.True(r.IsChild);
    Assert.Equal(10, r.ChildMaxAge);
  }

  [Fact]
  public void Combine_JellyfinStricterThanChildGroup_KeepsJellyfinLimit()
  {
    var r = ContentRestrictionPolicy.Combine(7, 1, false, false, 12);

    Assert.Equal(7, r.MaxScore);
    Assert.Equal(1, r.MaxSubScore);
    Assert.True(r.IsChild); // still a child account: search stays off, unrated stays hidden
    Assert.True(r.BlockUnratedShows);
  }

  [Fact]
  public void Combine_SameLimit_KeepsTheJellyfinSubScore()
  {
    var r = ContentRestrictionPolicy.Combine(12, 0, false, false, 12);

    Assert.Equal(12, r.MaxScore);
    Assert.Equal(0, r.MaxSubScore);
  }

  [Fact]
  public void Combine_AllAgesChildGroup_IsAScoreOfZero()
  {
    var r = ContentRestrictionPolicy.Combine(null, null, false, false, 0);

    Assert.Equal(0, r.MaxScore);
    Assert.True(r.IsRestricted);
  }

  [Theory]
  [InlineData(10, null, true)]  // below the limit
  [InlineData(13, null, true)]  // at the limit, no sub-score limit
  [InlineData(17, null, false)] // above the limit
  public void IsAllowed_ComparesTheScoreWithTheLimit(int score, int? subScore, bool expected)
  {
    var r = new ContentRestriction { MaxScore = 13 };

    Assert.Equal(expected, ContentRestrictionPolicy.IsAllowed(r, score, subScore, isMovie: true));
  }

  [Theory]
  [InlineData(0, true)]
  [InlineData(1, true)]
  [InlineData(2, false)]
  public void IsAllowed_AtTheLimit_TheSubScoreDecides(int subScore, bool expected)
  {
    // Jellyfin's rule: equal score -> allowed when the sub-score does not exceed the sub-score limit.
    var r = new ContentRestriction { MaxScore = 14, MaxSubScore = 1 };

    Assert.Equal(expected, ContentRestrictionPolicy.IsAllowed(r, 14, subScore, isMovie: false));
  }

  [Fact]
  public void IsAllowed_BelowTheLimit_IgnoresTheSubScore()
  {
    var r = new ContentRestriction { MaxScore = 14, MaxSubScore = 0 };

    Assert.True(ContentRestrictionPolicy.IsAllowed(r, 10, 5, isMovie: true));
  }

  [Fact]
  public void IsAllowed_Unrated_FollowsTheUnratedFlagOfItsKind()
  {
    var r = new ContentRestriction { MaxScore = 10, BlockUnratedMovies = true };

    Assert.False(ContentRestrictionPolicy.IsAllowed(r, null, null, isMovie: true));
    Assert.True(ContentRestrictionPolicy.IsAllowed(r, null, null, isMovie: false));
  }

  [Fact]
  public void IsAllowed_NoScoreLimit_OnlyUnratedFlagsApply()
  {
    var r = new ContentRestriction { BlockUnratedShows = true };

    Assert.True(ContentRestrictionPolicy.IsAllowed(r, 18, null, isMovie: false));
    Assert.False(ContentRestrictionPolicy.IsAllowed(r, null, null, isMovie: false));
  }

  [Fact]
  public void RatingsFor_PrefersTheServerCountry()
  {
    var ratings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
      ["FR"] = new[] { "12" },
      ["US"] = new[] { "R" },
    };

    var (country, picked) = ContentRestrictionPolicy.RatingsFor(ratings, "fr");

    Assert.Equal("FR", country);
    Assert.Equal(new[] { "12" }, picked);
  }

  [Fact]
  public void RatingsFor_FallsBackToTheUs()
  {
    var ratings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
      ["US"] = new[] { "PG-13" },
      ["DE"] = new[] { "12" },
    };

    var (country, picked) = ContentRestrictionPolicy.RatingsFor(ratings, "FR");

    Assert.Equal("US", country);
    Assert.Equal(new[] { "PG-13" }, picked);
  }

  [Fact]
  public void RatingsFor_RatedOnlyElsewhere_IsUnrated()
  {
    var ratings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
      ["DE"] = new[] { "16" },
    };

    var (_, picked) = ContentRestrictionPolicy.RatingsFor(ratings, "FR");

    Assert.Empty(picked);
  }

  [Fact]
  public void RatingsFor_NoServerCountry_UsesTheUs()
  {
    var ratings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
      ["US"] = new[] { "G" },
    };

    Assert.Equal("US", ContentRestrictionPolicy.RatingsFor(ratings, null).Country);
  }

  [Fact]
  public void Strictest_PicksTheHighestScoreThenSubScore()
  {
    var strictest = ContentRestrictionPolicy.Strictest(new (int, int?)[] { (10, null), (14, 0), (14, 1), (12, 3) });

    Assert.Equal((14, (int?)1), strictest);
  }

  [Fact]
  public void Strictest_NoScore_IsNull()
  {
    Assert.Null(ContentRestrictionPolicy.Strictest(Array.Empty<(int, int?)>()));
  }
}
