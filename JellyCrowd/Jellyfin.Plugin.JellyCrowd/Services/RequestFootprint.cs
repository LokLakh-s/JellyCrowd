using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// How much content a single request covers, in episodes. A request for a whole series or a whole season
/// downloads every episode it contains, so the quota must reserve that many estimates — reserving one
/// episode's worth for a thirty-episode series is what let a user commit far more disk than their quota.
/// Pure, so the arithmetic behind the quota gate is unit-tested.
/// </summary>
public static class RequestFootprint
{
  /// <summary>
  /// Counts the episodes a request covers. A movie or a single episode is always <c>1</c>; a season is
  /// that season's episode count; a whole-series request is the sum over its real seasons. Specials
  /// (season 0) are excluded — they are not fetched with a series. Falls back to <c>1</c> whenever the
  /// counts are unknown (no season list, or a provider that does not report counts), which keeps the
  /// previous behaviour rather than blocking a request on missing metadata.
  /// </summary>
  /// <param name="mediaType">The requested media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="season">The requested season, or <c>null</c> for a whole series.</param>
  /// <param name="episode">The requested episode, or <c>null</c> for a whole season/series.</param>
  /// <param name="seasons">The show's seasons, when known.</param>
  /// <returns>The number of episodes the request covers; at least <c>1</c>.</returns>
  public static int EpisodesCovered(string? mediaType, int? season, int? episode, IReadOnlyList<Season>? seasons)
  {
    // A movie, or one named episode: exactly one item either way.
    if (!string.Equals(mediaType, "tv", System.StringComparison.Ordinal) || episode is not null)
    {
      return 1;
    }

    if (seasons is null || seasons.Count == 0)
    {
      return 1;
    }

    var total = 0;
    foreach (var candidate in seasons)
    {
      if (season is int wanted)
      {
        if (candidate.SeasonNumber == wanted)
        {
          total += candidate.EpisodeCount ?? 0;
        }
      }
      else if (candidate.SeasonNumber > 0)
      {
        total += candidate.EpisodeCount ?? 0;
      }
    }

    return total > 0 ? total : 1;
  }
}
