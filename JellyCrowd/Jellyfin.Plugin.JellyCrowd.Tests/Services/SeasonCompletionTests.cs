using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="SeasonCompletion"/> — when a season or whole-series request counts as delivered.
/// </summary>
public class SeasonCompletionTests
{
  private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
  private static readonly TimeSpan Grace = TimeSpan.FromHours(48);

  private static HashSet<EpisodeKey> Keys(params (int S, int E)[] keys)
  {
    var set = new HashSet<EpisodeKey>();
    foreach (var (s, e) in keys)
    {
      set.Add(new EpisodeKey(s, e));
    }

    return set;
  }

  // A HashSet has no stable order, so compare episode sets as sequences sorted the same way on both
  // sides — comparing a set against a plain collection is otherwise undefined (xUnit2027).
  private static List<EpisodeKey> Ordered(IEnumerable<EpisodeKey> keys)
    => keys.OrderBy(k => k.Season).ThenBy(k => k.Episode).ToList();

  [Fact]
  public void AiredEpisodes_KeepsOnlyWhatHasAlreadyAired()
  {
    var episodes = new[]
    {
      new Episode { SeasonNumber = 1, EpisodeNumber = 1, AirDate = "2026-09-01" },
      new Episode { SeasonNumber = 1, EpisodeNumber = 2, AirDate = "2026-09-14" }, // today counts
      new Episode { SeasonNumber = 1, EpisodeNumber = 3, AirDate = "2026-09-21" }, // next week
      new Episode { SeasonNumber = 1, EpisodeNumber = 4, AirDate = null },          // unknown = not aired
    };

    Assert.Equal(Ordered(Keys((1, 1), (1, 2))), Ordered(SeasonCompletion.AiredEpisodes(episodes, Now, season: 1)));
  }

  [Fact]
  public void AiredEpisodes_ForAWholeSeries_LeavesSpecialsOut()
  {
    var episodes = new[]
    {
      new Episode { SeasonNumber = 0, EpisodeNumber = 1, AirDate = "2020-01-01" },
      new Episode { SeasonNumber = 1, EpisodeNumber = 1, AirDate = "2020-01-01" },
      new Episode { SeasonNumber = 2, EpisodeNumber = 1, AirDate = "2021-01-01" },
    };

    Assert.Equal(Ordered(Keys((1, 1), (2, 1))), Ordered(SeasonCompletion.AiredEpisodes(episodes, Now, season: null)));
  }

  [Fact]
  public void AiredEpisodes_ForOneSeason_IgnoresTheOtherSeasons()
  {
    var episodes = new[]
    {
      new Episode { SeasonNumber = 1, EpisodeNumber = 1, AirDate = "2020-01-01" },
      new Episode { SeasonNumber = 2, EpisodeNumber = 1, AirDate = "2020-01-01" },
    };

    Assert.Equal(Ordered(Keys((2, 1))), Ordered(SeasonCompletion.AiredEpisodes(episodes, Now, season: 2)));
  }

  [Fact]
  public void ShouldPromote_IsFalse_WhileNothingIsInTheLibrary()
    => Assert.False(SeasonCompletion.ShouldPromote(Keys(), Keys((1, 1)), Now.AddDays(-30), Now, Grace));

  [Fact]
  public void ShouldPromote_AsSoonAsEveryAiredEpisodeIsThere()
  {
    // Complete: no need to wait out the grace period, even if the last episode arrived a minute ago.
    Assert.True(SeasonCompletion.ShouldPromote(Keys((1, 1), (1, 2)), Keys((1, 1), (1, 2)), Now.AddMinutes(-1), Now, Grace));
  }

  [Fact]
  public void ShouldPromote_NotOnTheFirstEpisode_WhileTheRestIsStillArriving()
  {
    // The reported shape: 1 episode of 11 in, the rest still downloading. Promoting now would release the
    // reservation for the ten still to come.
    var present = Keys((1, 1));
    var aired = Keys((1, 1), (1, 2), (1, 3), (1, 4), (1, 5), (1, 6), (1, 7), (1, 8), (1, 9), (1, 10), (1, 11));

    Assert.False(SeasonCompletion.ShouldPromote(present, aired, Now.AddHours(-3), Now, Grace));
  }

  [Fact]
  public void ShouldPromote_SettlesForWhatIsThere_OnceNothingHasArrivedForTheGracePeriod()
  {
    // An episode the backend never finds must not keep the request pending forever.
    var present = Keys((1, 1), (1, 2));
    var aired = Keys((1, 1), (1, 2), (1, 3));

    Assert.False(SeasonCompletion.ShouldPromote(present, aired, Now - Grace + TimeSpan.FromMinutes(1), Now, Grace));
    Assert.True(SeasonCompletion.ShouldPromote(present, aired, Now - Grace, Now, Grace));
  }

  [Fact]
  public void ShouldPromote_FallsBackOnTheGracePeriod_WhenTmdbCannotListTheEpisodes()
  {
    var present = Keys((1, 1));

    Assert.False(SeasonCompletion.ShouldPromote(present, aired: null, Now.AddHours(-1), Now, Grace));
    Assert.True(SeasonCompletion.ShouldPromote(present, aired: null, Now.AddDays(-3), Now, Grace));
  }

  [Fact]
  public void ShouldPromote_ToleratesADifferentNumbering_ThroughTheGracePeriod()
  {
    // TMDB and the library number the season differently: completeness can never be proven by matching.
    var present = Keys((1, 13), (1, 14));
    var aired = Keys((1, 1), (1, 2));

    Assert.False(SeasonCompletion.ShouldPromote(present, aired, Now.AddHours(-1), Now, Grace));
    Assert.True(SeasonCompletion.ShouldPromote(present, aired, Now.AddDays(-3), Now, Grace));
  }
}
