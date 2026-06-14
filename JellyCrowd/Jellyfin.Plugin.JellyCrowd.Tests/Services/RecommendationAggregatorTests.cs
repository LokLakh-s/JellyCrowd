using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RecommendationAggregator"/>.
/// </summary>
public class RecommendationAggregatorTests
{
  [Fact]
  public void Aggregate_RanksByFrequencyThenRating_AndExcludes()
  {
    var candidates = new List<CatalogItem>
    {
      new() { TmdbId = 1, MediaType = "movie", Title = "A", VoteAverage = 5 },
      new() { TmdbId = 1, MediaType = "movie", Title = "A", VoteAverage = 5 }, // recommended twice
      new() { TmdbId = 2, MediaType = "movie", Title = "B", VoteAverage = 9 }, // once, high rating
      new() { TmdbId = 3, MediaType = "movie", Title = "Owned", VoteAverage = 10 } // excluded
    };
    var exclude = new HashSet<string> { RecommendationAggregator.Key("movie", 3) };

    var result = RecommendationAggregator.Aggregate(candidates, exclude, 10);

    Assert.Equal(new[] { "A", "B" }, result.Select(i => i.Title).ToArray());
  }

  [Fact]
  public void Aggregate_RespectsMaxAndDropsInvalid()
  {
    var candidates = new List<CatalogItem>
    {
      new() { TmdbId = 1, MediaType = "movie", Title = "A", VoteAverage = 1 },
      new() { TmdbId = 2, MediaType = "movie", Title = "B", VoteAverage = 2 },
      new() { TmdbId = 0, MediaType = "movie", Title = "Invalid" }
    };

    var result = RecommendationAggregator.Aggregate(candidates, new HashSet<string>(), 1);

    Assert.Single(result);
    Assert.DoesNotContain(result, i => i.Title == "Invalid");
  }
}
