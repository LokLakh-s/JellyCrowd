using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure aggregation of <see cref="PlaybackRecord"/>s into the statistics overview (totals, top movies,
/// top shows, top users, recent activity). Network- and Jellyfin-free so it can be unit tested; the
/// library counts are filled in by the caller.
/// </summary>
public static class StatsAggregator
{
  /// <summary>
  /// Builds the playback-derived parts of the overview from the given records.
  /// </summary>
  /// <param name="records">The playback records to aggregate.</param>
  /// <param name="topN">How many entries to keep in each top list.</param>
  /// <param name="recentN">How many recent plays to include.</param>
  /// <returns>The overview with the playback parts populated (library counts left at 0).</returns>
  public static StatsOverviewDto BuildOverview(IReadOnlyList<PlaybackRecord> records, int topN, int recentN)
  {
    ArgumentNullException.ThrowIfNull(records);
    if (topN <= 0)
    {
      topN = 5;
    }

    if (recentN <= 0)
    {
      recentN = 15;
    }

    var dto = new StatsOverviewDto
    {
      TotalPlays = records.Count,
      TotalMinutes = Math.Round(records.Sum(r => r.Minutes), 1),
      UniqueUsers = records.Select(r => r.UserId).Distinct().Count()
    };

    dto.TopMovies = records
      .Where(r => string.Equals(r.ItemType, "Movie", StringComparison.OrdinalIgnoreCase))
      .GroupBy(r => r.ItemId)
      .Select(g => new StatsItemDto
      {
        ItemId = g.Key,
        Name = g.Select(r => r.ItemName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? string.Empty,
        Plays = g.Count(),
        Minutes = Math.Round(g.Sum(r => r.Minutes), 1)
      })
      .OrderByDescending(i => i.Plays).ThenByDescending(i => i.Minutes)
      .Take(topN).ToList();

    dto.TopShows = records
      .Where(r => string.Equals(r.ItemType, "Episode", StringComparison.OrdinalIgnoreCase))
      .GroupBy(r => !string.IsNullOrEmpty(r.SeriesId) ? r.SeriesId : r.SeriesName)
      .Select(g => new StatsItemDto
      {
        ItemId = g.Select(r => r.SeriesId).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? string.Empty,
        Name = g.Select(r => r.SeriesName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? string.Empty,
        Plays = g.Count(),
        Minutes = Math.Round(g.Sum(r => r.Minutes), 1)
      })
      .OrderByDescending(i => i.Plays).ThenByDescending(i => i.Minutes)
      .Take(topN).ToList();

    dto.TopUsers = records
      .GroupBy(r => r.UserId)
      .Select(g => new StatsUserDto
      {
        UserId = g.Key,
        Name = g.Select(r => r.UserName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? string.Empty,
        Plays = g.Count(),
        Minutes = Math.Round(g.Sum(r => r.Minutes), 1)
      })
      .OrderByDescending(u => u.Minutes).ThenByDescending(u => u.Plays)
      .Take(topN).ToList();

    dto.Recent = records
      .OrderByDescending(r => r.PlayedAtUtc)
      .Take(recentN)
      .Select(r => new StatsRecentDto
      {
        UserName = r.UserName,
        Label = Label(r),
        Type = r.ItemType,
        PlayedAtUtc = r.PlayedAtUtc,
        Minutes = Math.Round(r.Minutes, 1)
      })
      .ToList();

    return dto;
  }

  /// <summary>
  /// Builds the activity-over-time series: plays and watched minutes per UTC day for the last
  /// <paramref name="days"/> days ending on <paramref name="nowUtc"/>, oldest first, zero-filled.
  /// </summary>
  /// <param name="records">The playback records.</param>
  /// <param name="nowUtc">The current UTC time (the series ends on this day).</param>
  /// <param name="days">The number of days to include.</param>
  /// <returns>One entry per day, ascending.</returns>
  public static IReadOnlyList<StatsDayDto> BuildDailySeries(IReadOnlyList<PlaybackRecord> records, DateTime nowUtc, int days)
  {
    ArgumentNullException.ThrowIfNull(records);
    if (days <= 0)
    {
      days = 30;
    }

    if (days > 366)
    {
      days = 366;
    }

    var byDay = records
      .GroupBy(r => r.PlayedAtUtc.Date)
      .ToDictionary(g => g.Key, g => (Plays: g.Count(), Minutes: Math.Round(g.Sum(r => r.Minutes), 1)));

    var today = nowUtc.Date;
    var result = new List<StatsDayDto>(days);
    for (var i = days - 1; i >= 0; i--)
    {
      var day = today.AddDays(-i);
      byDay.TryGetValue(day, out var v);
      result.Add(new StatsDayDto
      {
        Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Plays = v.Plays,
        Minutes = v.Minutes
      });
    }

    return result;
  }

  // "Dune" for a movie; "The Office · S2E5" for an episode (falling back to the episode title).
  private static string Label(PlaybackRecord r)
  {
    if (string.Equals(r.ItemType, "Episode", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(r.SeriesName))
    {
      var code = string.Empty;
      if (r.Season.HasValue && r.Episode.HasValue)
      {
        code = string.Format(CultureInfo.InvariantCulture, " · S{0}E{1}", r.Season.Value, r.Episode.Value);
      }

      return r.SeriesName + code;
    }

    return r.ItemName;
  }
}
