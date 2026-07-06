using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="MediaScope.Overlaps"/> — the per-season/episode overlap rule that drives ownership,
/// orphan detection and the shared-media deletion guard.
/// </summary>
public class MediaScopeTests
{
  [Theory]
  // Whole series (null season) overlaps anything of that show.
  [InlineData(null, null, null, null, true)]
  [InlineData(null, null, 1, null, true)]
  [InlineData(2, 3, null, null, true)]
  // Different seasons never overlap — this is what unblocks per-season deletion.
  [InlineData(1, null, 2, null, false)]
  [InlineData(1, 4, 2, 4, false)]
  // Same season overlaps: whole season vs whole season, whole season vs an episode.
  [InlineData(1, null, 1, null, true)]
  [InlineData(1, null, 1, 5, true)]
  [InlineData(1, 5, 1, null, true)]
  // Same season, same episode overlaps; same season, different episode does not.
  [InlineData(1, 5, 1, 5, true)]
  [InlineData(1, 5, 1, 6, false)]
  public void Overlaps_MatchesTheScopeRules(int? seasonA, int? episodeA, int? seasonB, int? episodeB, bool expected)
  {
    Assert.Equal(expected, MediaScope.Overlaps(seasonA, episodeA, seasonB, episodeB));
    Assert.Equal(expected, MediaScope.Overlaps(seasonB, episodeB, seasonA, episodeA)); // symmetric
  }
}
