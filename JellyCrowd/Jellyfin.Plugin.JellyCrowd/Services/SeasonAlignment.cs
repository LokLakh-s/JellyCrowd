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
  /// Reads a Sonarr episode list per season: each dated episode's number and air day.
  /// </summary>
  /// <param name="episodesJson">The raw <c>/episode?seriesId=…</c> JSON array.</param>
  /// <returns>The dated episodes by season number.</returns>
  public static IReadOnlyDictionary<int, IReadOnlyList<AiredEpisode>> ParseSonarrEpisodes(string? episodesJson)
  {
    var seasons = new Dictionary<int, List<AiredEpisode>>();
    if (!string.IsNullOrWhiteSpace(episodesJson))
    {
      using var doc = JsonDocument.Parse(episodesJson);
      if (doc.RootElement.ValueKind == JsonValueKind.Array)
      {
        foreach (var episode in doc.RootElement.EnumerateArray())
        {
          if (episode.ValueKind == JsonValueKind.Object
              && episode.TryGetProperty("seasonNumber", out var seasonElement) && seasonElement.TryGetInt32(out var season)
              && episode.TryGetProperty("episodeNumber", out var numberElement) && numberElement.TryGetInt32(out var number)
              && episode.TryGetProperty("airDate", out var dateElement) && dateElement.ValueKind == JsonValueKind.String
              && ParseDay(dateElement.GetString()) is { } day)
          {
            if (!seasons.TryGetValue(season, out var list))
            {
              list = new List<AiredEpisode>();
              seasons[season] = list;
            }

            list.Add(new AiredEpisode(number, day));
          }
        }
      }
    }

    return seasons.ToDictionary(s => s.Key, s => (IReadOnlyList<AiredEpisode>)s.Value);
  }

  /// <summary>
  /// Finds which Sonarr season holds each TMDB season: the one of the same number when its episodes air on the
  /// same days, else the only other one that does. Every TMDB season must have air dates and be found, no two
  /// in the same Sonarr season, and each TMDB episode Sonarr lists must air the same day under the same
  /// number — the mapping keeps episode numbers as they are.
  /// </summary>
  /// <param name="tmdbSeasons">TMDB's dated episodes, by season.</param>
  /// <param name="sonarrSeasons">Sonarr's dated episodes, by season.</param>
  /// <returns>The Sonarr season for each TMDB season, or <c>null</c> when they cannot be matched safely.</returns>
  public static IReadOnlyDictionary<int, int>? MapSeasons(
    IReadOnlyDictionary<int, IReadOnlyList<AiredEpisode>> tmdbSeasons,
    IReadOnlyDictionary<int, IReadOnlyList<AiredEpisode>> sonarrSeasons)
  {
    ArgumentNullException.ThrowIfNull(tmdbSeasons);
    ArgumentNullException.ThrowIfNull(sonarrSeasons);
    var map = new Dictionary<int, int>();
    foreach (var (season, episodes) in tmdbSeasons)
    {
      if (episodes.Count == 0)
      {
        return null;
      }

      var days = episodes.Select(e => e.Day).ToList();
      int target;
      if (sonarrSeasons.TryGetValue(season, out var same) && Overlap(days, same.Select(e => e.Day).ToList()))
      {
        target = season;
      }
      else
      {
        var others = sonarrSeasons
          .Where(s => s.Key > 0 && s.Key != season && Overlap(days, s.Value.Select(e => e.Day).ToList()))
          .Select(s => s.Key)
          .ToList();
        if (others.Count != 1)
        {
          return null;
        }

        target = others[0];
      }

      if (!EpisodesAgree(episodes, sonarrSeasons[target]))
      {
        return null;
      }

      map[season] = target;
    }

    return map.Count > 0 && map.Values.Distinct().Count() == map.Count ? map : null;
  }

  /// <summary>
  /// Determines whether a mapping still holds: each TMDB season it covers airs on the same days as its Sonarr
  /// season (a season TMDB has not dated yet proves nothing).
  /// </summary>
  /// <param name="mapping">The TMDB season → Sonarr season pairs to check.</param>
  /// <param name="tmdbSeasons">TMDB's dated episodes, by season.</param>
  /// <param name="sonarrSeasons">Sonarr's dated episodes, by season.</param>
  /// <returns><c>false</c> when a covered season now airs on other days.</returns>
  public static bool Holds(
    IEnumerable<(int TmdbSeason, int SonarrSeason)> mapping,
    IReadOnlyDictionary<int, IReadOnlyList<AiredEpisode>> tmdbSeasons,
    IReadOnlyDictionary<int, IReadOnlyList<AiredEpisode>> sonarrSeasons)
  {
    ArgumentNullException.ThrowIfNull(mapping);
    ArgumentNullException.ThrowIfNull(tmdbSeasons);
    ArgumentNullException.ThrowIfNull(sonarrSeasons);
    foreach (var (tmdbSeason, sonarrSeason) in mapping)
    {
      if (!tmdbSeasons.TryGetValue(tmdbSeason, out var episodes) || episodes.Count == 0)
      {
        continue;
      }

      if (!sonarrSeasons.TryGetValue(sonarrSeason, out var listed)
          || !Overlap(episodes.Select(e => e.Day).ToList(), listed.Select(e => e.Day).ToList()))
      {
        return false;
      }
    }

    return true;
  }

  /// <summary>
  /// Determines whether a TVDB series not in Sonarr yet could hold the requested episodes: they air within its
  /// run (first to last air date, give or take <see cref="StartTolerance"/>). A cheap test made before adding it
  /// to Sonarr just to compare its episodes.
  /// </summary>
  /// <param name="tmdbSeasons">TMDB's dated episodes, by season.</param>
  /// <param name="tvdbFirstAired">The series' first air date, as Sonarr reports it.</param>
  /// <param name="tvdbLastAired">The series' last air date, if any.</param>
  /// <returns><c>true</c> when the episodes fall within its run.</returns>
  public static bool WithinRun(IReadOnlyDictionary<int, IReadOnlyList<AiredEpisode>> tmdbSeasons, string? tvdbFirstAired, string? tvdbLastAired)
  {
    ArgumentNullException.ThrowIfNull(tmdbSeasons);
    var days = tmdbSeasons.Values.SelectMany(e => e).Select(e => e.Day).ToList();
    if (days.Count == 0 || ParseDay(tvdbFirstAired) is not { } first)
    {
      return false;
    }

    var last = ParseDay(tvdbLastAired) ?? DateTime.MaxValue.Date;
    return days.Min() >= first - StartTolerance && (last == DateTime.MaxValue.Date || days.Max() <= last + StartTolerance);
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

  // Each TMDB episode Sonarr lists under the same number airs the same day (a day apart is the same airing).
  private static bool EpisodesAgree(IReadOnlyList<AiredEpisode> tmdb, IReadOnlyList<AiredEpisode> sonarr)
  {
    foreach (var episode in tmdb)
    {
      var listed = sonarr.FirstOrDefault(e => e.Number == episode.Number);
      if (listed is not null && (listed.Day - episode.Day).Duration() > TimeSpan.FromDays(1))
      {
        return false;
      }
    }

    return true;
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
