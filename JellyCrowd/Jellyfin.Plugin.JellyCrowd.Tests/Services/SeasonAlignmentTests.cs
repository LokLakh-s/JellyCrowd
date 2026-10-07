using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="SeasonAlignment"/>, on the reported case: TMDB lists "Monster: The Lizzie Borden Story"
/// as a show of its own (one season, from 2026-09-17), TVDB as season 4 of "Monster (2022)".
/// </summary>
public class SeasonAlignmentTests
{
  private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

  private static IReadOnlyList<DateTime> Weekly(DateTime first, int count) => Enumerable.Range(0, count).Select(i => first.AddDays(7 * i)).ToList();

  private static IReadOnlyDictionary<int, IReadOnlyList<DateTime>> Monster() => new Dictionary<int, IReadOnlyList<DateTime>>
  {
    [1] = Weekly(Day(2022, 9, 21), 10),
    [2] = Weekly(Day(2024, 9, 19), 9),
    [3] = Weekly(Day(2025, 10, 3), 8),
    [4] = Weekly(Day(2026, 9, 17), 8)
  };

  [Fact]
  public void Check_TmdbSeasonThatIsAnotherSeasonThere_IsAMismatch_NamingIt()
  {
    var (verdict, elsewhere) = SeasonAlignment.Check(1, Weekly(Day(2026, 9, 17), 8), Monster());

    Assert.Equal(SeasonAlignment.Verdict.Mismatch, verdict);
    Assert.Equal(4, elsewhere);
  }

  [Fact]
  public void Check_SameEpisodes_AreAligned_EvenADayApart()
  {
    // An airing seen from another time zone lands on the next day.
    var (verdict, _) = SeasonAlignment.Check(4, Weekly(Day(2026, 9, 18), 8), Monster());
    Assert.Equal(SeasonAlignment.Verdict.Aligned, verdict);
  }

  [Fact]
  public void Check_NoTmdbDates_IsUnknown()
  {
    Assert.Equal(SeasonAlignment.Verdict.Unknown, SeasonAlignment.Check(1, Array.Empty<DateTime>(), Monster()).Verdict);
  }

  [Fact]
  public void Check_SeasonSonarrDoesNotListYet_IsUnknown()
  {
    // A new season TMDB announced before TVDB: nothing contradicts the numbering.
    Assert.Equal(SeasonAlignment.Verdict.Unknown, SeasonAlignment.Check(5, Weekly(Day(2027, 9, 1), 8), Monster()).Verdict);
  }

  [Fact]
  public void Check_SameNumberWithOtherDates_AndNoMatchElsewhere_IsAMismatch()
  {
    var (verdict, elsewhere) = SeasonAlignment.Check(1, Weekly(Day(2019, 1, 1), 6), Monster());

    Assert.Equal(SeasonAlignment.Verdict.Mismatch, verdict);
    Assert.Null(elsewhere);
  }

  [Fact]
  public void Check_PartlyListedSeason_IsAligned()
  {
    // Sonarr lists only the first episodes of a season TMDB lists in full.
    var sonarr = new Dictionary<int, IReadOnlyList<DateTime>> { [2] = Weekly(Day(2026, 3, 1), 3) };
    Assert.Equal(SeasonAlignment.Verdict.Aligned, SeasonAlignment.Check(2, Weekly(Day(2026, 3, 1), 10), sonarr).Verdict);
  }

  [Theory]
  [InlineData("2026-09-17", "2022-09-21T00:00:00Z", false)]
  [InlineData("2023-12-29", "2023-12-29T00:00:00Z", true)]
  [InlineData("2023-12-29", "2024-01-15T00:00:00Z", true)]
  [InlineData(null, "2022-09-21T00:00:00Z", true)]
  [InlineData("2026-09-17", null, true)]
  public void StartTogether_OnlyFirstAirDatesFarApartDisagree(string? tmdb, string? tvdb, bool expected)
  {
    Assert.Equal(expected, SeasonAlignment.StartTogether(tmdb, tvdb));
  }

  [Fact]
  public void ParseSonarrAirDates_GroupsDatedEpisodesBySeason()
  {
    const string Json = """
      [
        { "seasonNumber": 1, "episodeNumber": 1, "airDate": "2022-09-21" },
        { "seasonNumber": 1, "episodeNumber": 2, "airDate": "2022-09-21" },
        { "seasonNumber": 4, "episodeNumber": 1, "airDate": "2026-09-17" },
        { "seasonNumber": 4, "episodeNumber": 9 }
      ]
      """;

    var dates = SeasonAlignment.ParseSonarrAirDates(Json);

    Assert.Equal(2, dates[1].Count);
    Assert.Equal(new[] { Day(2026, 9, 17) }, dates[4]);
    Assert.Empty(SeasonAlignment.ParseSonarrAirDates(null));
  }

  private static IReadOnlyList<AiredEpisode> Drop(DateTime day, int count) => Enumerable.Range(1, count).Select(n => new AiredEpisode(n, day)).ToList();

  private static IReadOnlyList<AiredEpisode> WeeklyEpisodes(DateTime first, int count) => Enumerable.Range(1, count).Select(n => new AiredEpisode(n, first.AddDays(7 * (n - 1)))).ToList();

  [Fact]
  public void MapSeasons_BerlinsSpinOff_IsSeasonTwoOfBerlin()
  {
    // TMDB: "Berlin and the Lady with an Ermine", one season of 8 episodes released at once on 2026-05-15.
    // TVDB: season 2 of "Berlin (2023)".
    var tmdb = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [1] = Drop(Day(2026, 5, 15), 8) };
    var sonarr = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [1] = Drop(Day(2023, 12, 29), 8), [2] = Drop(Day(2026, 5, 15), 8) };

    var map = SeasonAlignment.MapSeasons(tmdb, sonarr);

    Assert.Equal(new Dictionary<int, int> { [1] = 2 }, map);
  }

  [Fact]
  public void MapSeasons_SameNumbering_MapsEachSeasonToItself()
  {
    var tmdb = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [1] = WeeklyEpisodes(Day(2024, 1, 1), 6), [2] = WeeklyEpisodes(Day(2025, 1, 1), 6) };
    var sonarr = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [1] = WeeklyEpisodes(Day(2024, 1, 1), 6), [2] = WeeklyEpisodes(Day(2025, 1, 1), 6) };

    Assert.Equal(new Dictionary<int, int> { [1] = 1, [2] = 2 }, SeasonAlignment.MapSeasons(tmdb, sonarr));
  }

  [Fact]
  public void MapSeasons_EpisodesUnderOtherNumbers_AreNotMapped()
  {
    // Same days, but Sonarr's episode 3 airs on TMDB's episode 4 day: numbers cannot be kept as they are.
    var tmdb = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [1] = WeeklyEpisodes(Day(2026, 9, 17), 4) };
    var shifted = WeeklyEpisodes(Day(2026, 9, 17), 4).Select(e => e with { Number = e.Number + 1 }).ToList();
    var sonarr = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [4] = shifted };

    Assert.Null(SeasonAlignment.MapSeasons(tmdb, sonarr));
  }

  [Fact]
  public void MapSeasons_TwoCandidateSeasons_AreNotGuessedBetween()
  {
    var tmdb = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [1] = Drop(Day(2026, 5, 15), 4) };
    var sonarr = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [2] = Drop(Day(2026, 5, 15), 4), [3] = Drop(Day(2026, 5, 15), 4) };

    Assert.Null(SeasonAlignment.MapSeasons(tmdb, sonarr));
  }

  [Fact]
  public void MapSeasons_UndatedSeason_CannotBeMapped()
  {
    var tmdb = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [1] = Array.Empty<AiredEpisode>() };
    var sonarr = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [2] = Drop(Day(2026, 5, 15), 4) };

    Assert.Null(SeasonAlignment.MapSeasons(tmdb, sonarr));
  }

  [Fact]
  public void Holds_FalseOnceTheSonarrSeasonAirsOnOtherDays()
  {
    var tmdb = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [1] = Drop(Day(2026, 5, 15), 8) };
    var sonarr = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [2] = Drop(Day(2026, 5, 15), 8), [3] = Drop(Day(2027, 5, 15), 8) };

    Assert.True(SeasonAlignment.Holds(new[] { (1, 2) }, tmdb, sonarr));
    Assert.False(SeasonAlignment.Holds(new[] { (1, 3) }, tmdb, sonarr));
  }

  [Theory]
  [InlineData("2023-12-29T00:00:00Z", "2026-05-15T00:00:00Z", true)]
  [InlineData("2023-12-29T00:00:00Z", null, true)]           // still running
  [InlineData("2027-01-01T00:00:00Z", null, false)]          // started after the episodes aired
  [InlineData("2009-01-01T00:00:00Z", "2015-03-20T00:00:00Z", false)] // ended long before
  [InlineData(null, null, false)]
  public void WithinRun_OnlyASeriesWhoseRunCoversTheEpisodes(string? first, string? last, bool expected)
  {
    var tmdb = new Dictionary<int, IReadOnlyList<AiredEpisode>> { [1] = Drop(Day(2026, 5, 15), 8) };
    Assert.Equal(expected, SeasonAlignment.WithinRun(tmdb, first, last));
  }

  [Fact]
  public void ParseSonarrEpisodes_KeepsNumbersAndDays()
  {
    const string Json = """[ { "seasonNumber": 2, "episodeNumber": 3, "airDate": "2026-05-15" }, { "seasonNumber": 2, "episodeNumber": 4 } ]""";

    var parsed = SeasonAlignment.ParseSonarrEpisodes(Json);

    Assert.Equal(new[] { new AiredEpisode(3, Day(2026, 5, 15)) }, parsed[2]);
  }
}
