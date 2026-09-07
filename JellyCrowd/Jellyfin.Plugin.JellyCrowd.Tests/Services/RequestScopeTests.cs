using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestScope"/> — which requests cover which, the rule behind both the quota
/// de-duplication and the refusal of overlapping requests.
/// </summary>
public class RequestScopeTests
{
  private static RequestScope Series(int tmdbId = 1) => new("tv", tmdbId, null, null);

  private static RequestScope Season(int season, int tmdbId = 1) => new("tv", tmdbId, season, null);

  private static RequestScope Episode(int season, int episode, int tmdbId = 1) => new("tv", tmdbId, season, episode);

  [Fact]
  public void Contains_IsReflexive_SoAnExactDuplicateCounts()
  {
    Assert.True(Series().Contains(Series()));
    Assert.True(Season(1).Contains(Season(1)));
    Assert.True(Episode(1, 2).Contains(Episode(1, 2)));
  }

  [Fact]
  public void WholeSeries_ContainsEverySeasonAndEpisode()
  {
    Assert.True(Series().Contains(Season(1)));
    Assert.True(Series().Contains(Season(3)));
    Assert.True(Series().Contains(Episode(2, 7)));
  }

  [Fact]
  public void ASeason_ContainsItsOwnEpisodesOnly()
  {
    Assert.True(Season(2).Contains(Episode(2, 1)));
    Assert.False(Season(2).Contains(Episode(3, 1)));
    Assert.False(Season(2).Contains(Season(3)));
  }

  [Fact]
  public void ANarrowScope_DoesNotContainABroaderOne()
  {
    Assert.False(Season(1).Contains(Series()));
    Assert.False(Episode(1, 1).Contains(Season(1)));
  }

  [Fact]
  public void ScopesOfDifferentTitlesNeverOverlap()
  {
    Assert.False(Series(tmdbId: 1).Contains(Series(tmdbId: 2)));
    Assert.False(Series(tmdbId: 1).Contains(Episode(1, 1, tmdbId: 2)));
    Assert.False(new RequestScope("movie", 1, null, null).Contains(new RequestScope("tv", 1, null, null)));
  }

  [Fact]
  public void Of_ReadsTheScopeOffAStoredRequest()
  {
    var scope = RequestScope.Of(new RequestRecord { MediaType = "tv", TmdbId = 42, Season = 2, Episode = 5 });

    Assert.Equal(new RequestScope("tv", 42, 2, 5), scope);
  }
}
