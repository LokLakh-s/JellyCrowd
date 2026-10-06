using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// TMDB and TVDB, which Sonarr follows, do not always split a show the same way: TMDB lists "Monster: The
/// Lizzie Borden Story" as a show of its own with one season, TVDB as season 4 of "Monster". Jelly Crowd hands
/// Sonarr TMDB's season numbers, so before monitoring anything it checks that they mean the same episodes —
/// by their air dates — and refuses rather than download the wrong season.
/// </summary>
public static class SeasonAlignment
{
  /// <summary>
  /// How far apart two first air dates may be and still be the same start (time zones, a pilot listed apart).
  /// </summary>
  public static readonly TimeSpan StartTolerance = TimeSpan.FromDays(31);

  /// <summary>
  /// The outcome of comparing one TMDB season with Sonarr's seasons.
  /// </summary>
  public enum Verdict
  {
    /// <summary>The Sonarr season of the same number holds the same episodes.</summary>
    Aligned,

    /// <summary>Nothing to compare yet (no air dates on one side): the numbering is trusted, as before.</summary>
    Unknown,

    /// <summary>The same number does not hold the same episodes in Sonarr.</summary>
    Mismatch
  }

  /// <summary>
  /// Determines whether a TMDB show and a TVDB series start together, from their first air dates. A series
  /// that started long before the show (or after it) is not the same show: TMDB's season 1 is a later season
  /// there. An unknown date proves nothing.
  /// </summary>
  /// <param name="tmdbFirstAirDate">The TMDB show's first air date.</param>
  /// <param name="tvdbFirstAired">The TVDB series' first air date, as Sonarr reports it.</param>
  /// <returns><c>false</c> only when both dates are known and too far apart.</returns>
  public static bool StartTogether(string? tmdbFirstAirDate, string? tvdbFirstAired)
  {
    if (ParseDay(tmdbFirstAirDate) is not { } tmdb || ParseDay(tvdbFirstAired) is not { } tvdb)
    {
      return true;
    }

    return (tmdb - tvdb).Duration() <= StartTolerance;
  }

  /// <summary>
  /// Reads the air dates of a Sonarr episode list, per season (specials included, undated episodes left out).
  /// </summary>
  /// <param name="episodesJson">The raw <c>/episode?seriesId=…</c> JSON array.</param>
  /// <returns>The air dates by season number.</returns>
  public static IReadOnlyDictionary<int, IReadOnlyList<DateTime>> ParseSonarrAirDates(string? episodesJson)
  {
    var seasons = new Dictionary<int, List<DateTime>>();
    if (string.IsNullOrWhiteSpace(episodesJson))
    {
      return new Dictionary<int, IReadOnlyList<DateTime>>();
    }

    using var doc = JsonDocument.Parse(episodesJson);
    if (doc.RootElement.ValueKind == JsonValueKind.Array)
    {
      foreach (var episode in doc.RootElement.EnumerateArray())
      {
        if (episode.ValueKind == JsonValueKind.Object
            && episode.TryGetProperty("seasonNumber", out var seasonElement) && seasonElement.TryGetInt32(out var season)
            && episode.TryGetProperty("airDate", out var dateElement) && dateElement.ValueKind == JsonValueKind.String
            && ParseDay(dateElement.GetString()) is { } day)
        {
          if (!seasons.TryGetValue(season, out var days))
          {
            days = new List<DateTime>();
            seasons[season] = days;
          }

          days.Add(day);
        }
      }
    }

    return seasons.ToDictionary(s => s.Key, s => (IReadOnlyList<DateTime>)s.Value);
  }

  /// <summary>
  /// Compares a TMDB season with Sonarr's seasons by their episodes' air dates.
  /// </summary>
  /// <param name="tmdbSeason">The TMDB season number.</param>
  /// <param name="tmdbAirDates">The air dates TMDB lists for its episodes.</param>
  /// <param name="sonarrAirDates">Sonarr's air dates by season.</param>
  /// <returns>The verdict and, on a mismatch, the Sonarr season holding those episodes, if one does.</returns>
  public static (Verdict Verdict, int? SonarrSeason) Check(int tmdbSeason, IReadOnlyList<DateTime> tmdbAirDates, IReadOnlyDictionary<int, IReadOnlyList<DateTime>> sonarrAirDates)
  {
    ArgumentNullException.ThrowIfNull(tmdbAirDates);
    ArgumentNullException.ThrowIfNull(sonarrAirDates);
    if (tmdbAirDates.Count == 0)
    {
      return (Verdict.Unknown, null);
    }

    if (sonarrAirDates.TryGetValue(tmdbSeason, out var same) && Overlap(tmdbAirDates, same))
    {
      return (Verdict.Aligned, null);
    }

    var elsewhere = sonarrAirDates
      .Where(s => s.Key > 0 && s.Key != tmdbSeason && Overlap(tmdbAirDates, s.Value))
      .Select(s => (int?)s.Key)
      .FirstOrDefault();
    if (elsewhere is not null)
    {
      return (Verdict.Mismatch, elsewhere);
    }

    // Sonarr lists that season with other dates: not the same episodes. Not listed at all (a new season TVDB
    // has not added yet), there is nothing to contradict the numbering.
    return (same is { Count: > 0 } ? Verdict.Mismatch : Verdict.Unknown, null);
  }

  /// <summary>
  /// Parses a date (<c>yyyy-MM-dd</c>, or a full timestamp) to its UTC day.
  /// </summary>
  /// <param name="value">The date text.</param>
  /// <returns>The UTC day, or <c>null</c>.</returns>
  public static DateTime? ParseDay(string? value)
  {
    if (string.IsNullOrWhiteSpace(value)
        || !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
    {
      return null;
    }

    return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
  }

  // Two seasons are the same episodes when at least half of the shorter list airs on the same days (a day
  // apart is the same airing, seen from another time zone).
  private static bool Overlap(IReadOnlyList<DateTime> a, IReadOnlyList<DateTime> b)
  {
    if (a.Count == 0 || b.Count == 0)
    {
      return false;
    }

    var matched = a.Count(day => b.Any(other => (day - other).Duration() <= TimeSpan.FromDays(1)));
    return matched > 0 && matched * 2 >= Math.Min(a.Count, b.Count);
  }
}
