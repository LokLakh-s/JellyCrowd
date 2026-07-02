using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="EmptySeriesPolicy"/>.
/// </summary>
public class EmptySeriesPolicyTests
{
  private static readonly DateTime Now = new(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);
  private static readonly TimeSpan MinAge = TimeSpan.FromHours(24);
  private static readonly IReadOnlySet<int> NoneWanted = new HashSet<int>();

  [Fact]
  public void Keeps_SeriesWithEpisodes()
  {
    Assert.False(EmptySeriesPolicy.ShouldRemove(3, Now.AddDays(-10), Now, MinAge, 1, NoneWanted));
  }

  [Fact]
  public void Removes_EmptyOldSeries_WithNoActiveRequest()
  {
    Assert.True(EmptySeriesPolicy.ShouldRemove(0, Now.AddDays(-2), Now, MinAge, 1, NoneWanted));
  }

  [Fact]
  public void Keeps_EmptySeries_TooRecent()
  {
    // A just-added series may be mid-download — never delete it before the grace period.
    Assert.False(EmptySeriesPolicy.ShouldRemove(0, Now.AddHours(-1), Now, MinAge, 1, NoneWanted));
  }

  [Fact]
  public void Keeps_EmptySeries_WantedByActiveRequest()
  {
    var wanted = new HashSet<int> { 42 };
    Assert.False(EmptySeriesPolicy.ShouldRemove(0, Now.AddDays(-5), Now, MinAge, 42, wanted));
  }

  [Fact]
  public void Removes_EmptyOldSeries_WithNoTmdbId()
  {
    // A ghost with no TMDB id (can't be matched to a request) is still removed once empty + old.
    Assert.True(EmptySeriesPolicy.ShouldRemove(0, Now.AddDays(-5), Now, MinAge, null, new HashSet<int> { 42 }));
  }

  [Fact]
  public void EmptinessCheck_TakesPrecedence_OverAge()
  {
    // A series that still has episodes is kept even when very old.
    Assert.False(EmptySeriesPolicy.ShouldRemove(1, Now.AddDays(-100), Now, MinAge, 1, NoneWanted));
  }
}
