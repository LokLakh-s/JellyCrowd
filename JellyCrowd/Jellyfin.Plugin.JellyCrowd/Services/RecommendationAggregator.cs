using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure aggregation for the "For you" recommendations row: merges TMDB recommendation results from
/// several seed titles, drops excluded titles (already requested/followed), and ranks by how often a
/// title is recommended (then by rating).
/// </summary>
public static class RecommendationAggregator
{
  /// <summary>
  /// Builds the "key" used for de-duplication and exclusion (<c>mediaType:tmdbId</c>).
  /// </summary>
  /// <param name="mediaType">The media type.</param>
  /// <param name="tmdbId">The TMDB id.</param>
  /// <returns>The composite key.</returns>
  public static string Key(string mediaType, int tmdbId)
    => mediaType + ":" + tmdbId.ToString(CultureInfo.InvariantCulture);

  /// <summary>
  /// Aggregates candidate recommendations into a ranked, de-duplicated list.
  /// </summary>
  /// <param name="candidates">All recommended items (may repeat across seeds).</param>
  /// <param name="excludeKeys">Keys to drop (seeds and already requested/followed titles).</param>
  /// <param name="max">Maximum number of results.</param>
  /// <returns>The top recommendations, most-recommended first.</returns>
  public static IReadOnlyList<CatalogItem> Aggregate(IEnumerable<CatalogItem> candidates, ISet<string> excludeKeys, int max)
  {
    ArgumentNullException.ThrowIfNull(candidates);
    ArgumentNullException.ThrowIfNull(excludeKeys);

    var byKey = new Dictionary<string, (CatalogItem Item, int Count)>(StringComparer.Ordinal);
    foreach (var candidate in candidates)
    {
      if (candidate is null || string.IsNullOrEmpty(candidate.MediaType) || candidate.TmdbId <= 0)
      {
        continue;
      }

      var key = Key(candidate.MediaType, candidate.TmdbId);
      if (excludeKeys.Contains(key))
      {
        continue;
      }

      byKey[key] = byKey.TryGetValue(key, out var existing)
        ? (existing.Item, existing.Count + 1)
        : (candidate, 1);
    }

    if (max <= 0)
    {
      return new List<CatalogItem>();
    }

    return byKey.Values
      .OrderByDescending(v => v.Count)
      .ThenByDescending(v => v.Item.VoteAverage)
      .Select(v => v.Item)
      .Take(max)
      .ToList();
  }
}
