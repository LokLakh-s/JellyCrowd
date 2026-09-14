using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// When a season or whole-series request counts as delivered. Marking it available as soon as its first
/// episode landed released its quota reservation while the rest was still downloading, started its expiry
/// clock on day one, and made a series with a missing season look complete. Pure, so the rule is
/// unit-tested.
/// </summary>
public static class SeasonCompletion
{
  /// <summary>
  /// The episodes of a scope that have already aired. A whole-series scope ignores specials (season 0),
  /// which are not fetched with a series; an episode with no air date is treated as not aired yet.
  /// </summary>
  /// <param name="episodes">The show's episodes, as listed by TMDB.</param>
  /// <param name="todayUtc">The current date.</param>
  /// <param name="season">The requested season, or <c>null</c> for the whole series.</param>
  /// <returns>The aired episodes within the scope.</returns>
  public static IReadOnlyCollection<EpisodeKey> AiredEpisodes(IEnumerable<Episode> episodes, DateTime todayUtc, int? season)
  {
    ArgumentNullException.ThrowIfNull(episodes);
    var aired = new HashSet<EpisodeKey>();
    foreach (var episode in episodes)
    {
      var inScope = season is int wanted ? episode.SeasonNumber == wanted : episode.SeasonNumber > 0;
      if (inScope
        && DateTime.TryParseExact(episode.AirDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var airDate)
        && airDate.Date <= todayUtc.Date)
      {
        aired.Add(new EpisodeKey(episode.SeasonNumber, episode.EpisodeNumber));
      }
    }

    return aired;
  }

  /// <summary>
  /// Whether an in-flight season or series request should now be marked available: every aired episode is
  /// in the library, or some are and none has arrived for <paramref name="grace"/>. The second case covers
  /// an episode the backend will never find, and a show numbered differently in TMDB and in the library,
  /// where completeness can never be proven — neither may keep a request pending forever.
  /// </summary>
  /// <param name="present">The scope's episodes currently in the library.</param>
  /// <param name="aired">The scope's aired episodes, or <c>null</c> when TMDB could not be read.</param>
  /// <param name="progressAt">When the last episode arrived, or <c>null</c> when none has.</param>
  /// <param name="nowUtc">The current time.</param>
  /// <param name="grace">How long to wait after the last arrival before settling for what is there.</param>
  /// <returns><c>true</c> when the request should be marked available.</returns>
  public static bool ShouldPromote(
    IReadOnlyCollection<EpisodeKey> present,
    IReadOnlyCollection<EpisodeKey>? aired,
    DateTime? progressAt,
    DateTime nowUtc,
    TimeSpan grace)
  {
    ArgumentNullException.ThrowIfNull(present);
    if (present.Count == 0)
    {
      return false;
    }

    if (aired is { Count: > 0 })
    {
      var onDisk = present as HashSet<EpisodeKey> ?? new HashSet<EpisodeKey>(present);
      var complete = true;
      foreach (var key in aired)
      {
        if (!onDisk.Contains(key))
        {
          complete = false;
          break;
        }
      }

      if (complete)
      {
        return true;
      }
    }

    return progressAt is DateTime since && nowUtc - since >= grace;
  }
}
