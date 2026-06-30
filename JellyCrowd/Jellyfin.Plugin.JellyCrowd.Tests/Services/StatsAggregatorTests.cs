using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="StatsAggregator"/>.
/// </summary>
public class StatsAggregatorTests
{
  private static readonly Guid Alice = Guid.NewGuid();
  private static readonly Guid Bob = Guid.NewGuid();

  private static PlaybackRecord Movie(Guid user, string userName, string id, string name, double mins, DateTime at)
    => new() { UserId = user, UserName = userName, ItemId = id, ItemName = name, ItemType = "Movie", Minutes = mins, PlayedAtUtc = at };

  private static PlaybackRecord Episode(Guid user, string userName, string seriesId, string series, int s, int e, double mins, DateTime at)
    => new() { UserId = user, UserName = userName, ItemId = Guid.NewGuid().ToString("N"), ItemName = "Ep", ItemType = "Episode", SeriesId = seriesId, SeriesName = series, Season = s, Episode = e, Minutes = mins, PlayedAtUtc = at };

  [Fact]
  public void BuildOverview_ComputesTotalsAndUniqueUsers()
  {
    var t = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    var records = new List<PlaybackRecord>
    {
      Movie(Alice, "Alice", "m1", "Dune", 120, t),
      Movie(Bob, "Bob", "m1", "Dune", 60, t.AddHours(1)),
      Movie(Alice, "Alice", "m2", "Heat", 30, t.AddHours(2))
    };

    var o = StatsAggregator.BuildOverview(records, 5, 10);

    Assert.Equal(3, o.TotalPlays);
    Assert.Equal(210, o.TotalMinutes);
    Assert.Equal(2, o.UniqueUsers);
  }

  [Fact]
  public void BuildOverview_TopMovies_GroupedAndOrderedByPlays()
  {
    var t = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    var records = new List<PlaybackRecord>
    {
      Movie(Alice, "Alice", "m1", "Dune", 100, t),
      Movie(Bob, "Bob", "m1", "Dune", 100, t),     // Dune: 2 plays
      Movie(Alice, "Alice", "m2", "Heat", 200, t)  // Heat: 1 play, more minutes
    };

    var o = StatsAggregator.BuildOverview(records, 5, 10);

    Assert.Equal(2, o.TopMovies.Count);
    Assert.Equal("Dune", o.TopMovies[0].Name); // 2 plays wins over Heat's 1 (despite fewer minutes)
    Assert.Equal(2, o.TopMovies[0].Plays);
    Assert.Equal(200, o.TopMovies[0].Minutes);
  }

  [Fact]
  public void BuildOverview_TopShows_GroupsEpisodesBySeries()
  {
    var t = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    var records = new List<PlaybackRecord>
    {
      Episode(Alice, "Alice", "s1", "The Office", 1, 1, 25, t),
      Episode(Alice, "Alice", "s1", "The Office", 1, 2, 25, t.AddMinutes(30)),
      Episode(Bob, "Bob", "s2", "Friends", 3, 5, 22, t)
    };

    var o = StatsAggregator.BuildOverview(records, 5, 10);

    Assert.Empty(o.TopMovies);
    var office = Assert.Single(o.TopShows, s => s.Name == "The Office");
    Assert.Equal(2, office.Plays);  // two episodes grouped under the series
    Assert.Equal(50, office.Minutes);
  }

  [Fact]
  public void BuildOverview_TopUsers_OrderedByWatchTime()
  {
    var t = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    var records = new List<PlaybackRecord>
    {
      Movie(Alice, "Alice", "m1", "Dune", 200, t),
      Movie(Bob, "Bob", "m2", "Heat", 50, t),
      Movie(Bob, "Bob", "m3", "Sicario", 40, t)
    };

    var o = StatsAggregator.BuildOverview(records, 5, 10);

    Assert.Equal("Alice", o.TopUsers[0].Name); // 200 min > Bob's 90
    Assert.Equal(2, o.TopUsers[1].Plays);       // Bob: 2 plays
  }

  [Fact]
  public void BuildDailySeries_ZeroFillsAndOrdersOldestFirst()
  {
    var now = new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Utc);
    var records = new List<PlaybackRecord>
    {
      Movie(Alice, "Alice", "m1", "Dune", 60, new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc)),  // today
      Movie(Bob, "Bob", "m2", "Heat", 30, new DateTime(2026, 6, 10, 11, 0, 0, DateTimeKind.Utc)),     // today
      Movie(Alice, "Alice", "m3", "Sicario", 90, new DateTime(2026, 6, 8, 20, 0, 0, DateTimeKind.Utc)) // 2 days ago
    };

    var series = StatsAggregator.BuildDailySeries(records, now, 5); // Jun 6..10

    Assert.Equal(5, series.Count);
    Assert.Equal("2026-06-06", series[0].Date); // oldest first
    Assert.Equal("2026-06-10", series[4].Date); // ends today
    Assert.Equal(0, series[0].Plays);            // empty day zero-filled
    Assert.Equal(1, series[2].Plays);            // Jun 8: one play
    Assert.Equal(90, series[2].Minutes);
    Assert.Equal(2, series[4].Plays);            // today: two plays
    Assert.Equal(90, series[4].Minutes);
  }

  [Fact]
  public void BuildOverview_Recent_NewestFirstWithEpisodeLabel()
  {
    var t = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    var records = new List<PlaybackRecord>
    {
      Movie(Alice, "Alice", "m1", "Dune", 100, t),
      Episode(Bob, "Bob", "s1", "The Office", 2, 5, 25, t.AddHours(3))
    };

    var o = StatsAggregator.BuildOverview(records, 5, 10);

    Assert.Equal("The Office · S2E5", o.Recent[0].Label); // newest first, episode formatted
    Assert.Equal("Dune", o.Recent[1].Label);
  }
}
