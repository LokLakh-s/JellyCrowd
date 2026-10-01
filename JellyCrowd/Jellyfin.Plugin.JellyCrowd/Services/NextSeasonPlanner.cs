using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure decisions behind the automatic next-season request: whether the episode being played is close
/// enough to the end of its season, which season comes next, and how to request it.
/// </summary>
public static class NextSeasonPlanner
{
  /// <summary>
  /// The highest "episodes left" threshold an administrator can set.
  /// </summary>
  public const int MaxEpisodesLeft = 10;

  /// <summary>
  /// The season to request when a user plays an episode, or <c>null</c> when nothing should be requested:
  /// a special or unnumbered episode, a current season whose length is unknown, more episodes left than the
  /// threshold, or no next season — an announced season known to be empty does not count.
  /// </summary>
  /// <param name="seasons">The show's seasons, numbered as in the library.</param>
  /// <param name="season">The season of the episode being played.</param>
  /// <param name="episode">The number of the episode being played.</param>
  /// <param name="episodesLeft">How many episodes may remain after it (clamped to 0–<see cref="MaxEpisodesLeft"/>).</param>
  /// <returns>The next season's number, or <c>null</c>.</returns>
  public static int? NextSeasonToRequest(IReadOnlyList<Season> seasons, int season, int episode, int episodesLeft)
  {
    ArgumentNullException.ThrowIfNull(seasons);
    if (season <= 0 || episode <= 0)
    {
      return null;
    }

    var current = seasons.FirstOrDefault(s => s.SeasonNumber == season);
    if (current?.EpisodeCount is not int count || count <= 0)
    {
      return null;
    }

    if (count - episode > Math.Clamp(episodesLeft, 0, MaxEpisodesLeft))
    {
      return null;
    }

    var next = seasons.FirstOrDefault(s => s.SeasonNumber == season + 1);
    return next is null || next.EpisodeCount == 0 ? null : season + 1;
  }

  /// <summary>
  /// How to request a season, exactly as the catalog does when a user clicks it: while some of its episodes
  /// have yet to air, one request per episode, each scheduled on its air date; otherwise a single request
  /// for the whole season (also when its episodes are not listed yet).
  /// </summary>
  /// <param name="episodes">The season's episodes.</param>
  /// <param name="todayUtc">The current UTC date.</param>
  /// <returns>The requests to make: an episode number with its air date, or a <c>null</c> episode for the whole season.</returns>
  public static IReadOnlyList<(int? Episode, string? AirDate)> PlanRequests(IReadOnlyList<Episode> episodes, DateTime todayUtc)
  {
    ArgumentNullException.ThrowIfNull(episodes);
    var numbered = episodes.Where(e => e.EpisodeNumber > 0).ToList();
    var hasFuture = numbered.Any(e => RequestScheduling.ParseReleaseDate(e.AirDate) is DateTime air && air.Date > todayUtc.Date);
    if (numbered.Count == 0 || !hasFuture)
    {
      return new[] { ((int?)null, (string?)null) };
    }

    return numbered
      .Select(e => ((int?)e.EpisodeNumber, e.AirDate))
      .ToList();
  }
}
