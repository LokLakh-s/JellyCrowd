using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Keeps the shows whose number of seasons falls in a range. <c>/discover/tv</c> has no such filter, so
/// each show's season list is looked up (cached by <see cref="CachingTmdbClient"/>) and the page is
/// filtered afterwards — a page can therefore come back shorter, or empty, while later ones still match.
/// </summary>
public static class SeasonRangeFilter
{
  // Concurrent season lookups per page: a page is 20 shows, and TMDB rate-limits bursts.
  private const int MaxParallelLookups = 4;

  /// <summary>
  /// Tells whether a range restricts anything.
  /// </summary>
  /// <param name="minSeasons">The minimum number of seasons, or null.</param>
  /// <param name="maxSeasons">The maximum number of seasons, or null.</param>
  /// <returns><c>true</c> when at least one bound is set.</returns>
  public static bool IsActive(int? minSeasons, int? maxSeasons) => minSeasons.HasValue || maxSeasons.HasValue;

  /// <summary>
  /// Tells whether a range is usable: bounds of at least 1, the minimum not above the maximum.
  /// </summary>
  /// <param name="minSeasons">The minimum number of seasons, or null.</param>
  /// <param name="maxSeasons">The maximum number of seasons, or null.</param>
  /// <returns><c>true</c> when the range is valid (an unset range is valid).</returns>
  public static bool IsValid(int? minSeasons, int? maxSeasons)
    => minSeasons is null or >= 1
      && maxSeasons is null or >= 1
      && !(minSeasons.HasValue && maxSeasons.HasValue && minSeasons.Value > maxSeasons.Value);

  /// <summary>
  /// Counts a show's seasons the way TMDB's <c>number_of_seasons</c> does: specials (season 0) excluded.
  /// </summary>
  /// <param name="seasons">The show's seasons.</param>
  /// <returns>The number of regular seasons.</returns>
  public static int CountSeasons(IEnumerable<Season> seasons)
  {
    ArgumentNullException.ThrowIfNull(seasons);
    return seasons.Count(s => s.SeasonNumber > 0);
  }

  /// <summary>
  /// Tells whether a season count falls within the range (bounds inclusive).
  /// </summary>
  /// <param name="count">The number of seasons.</param>
  /// <param name="minSeasons">The minimum number of seasons, or null.</param>
  /// <param name="maxSeasons">The maximum number of seasons, or null.</param>
  /// <returns><c>true</c> when the count is in range.</returns>
  public static bool InRange(int count, int? minSeasons, int? maxSeasons)
    => (minSeasons is null || count >= minSeasons.Value) && (maxSeasons is null || count <= maxSeasons.Value);

  /// <summary>
  /// Filters shows by number of seasons, keeping their order. A show whose seasons cannot be fetched is
  /// left out rather than failing the whole page.
  /// </summary>
  /// <param name="items">The shows to filter.</param>
  /// <param name="minSeasons">The minimum number of seasons, or null.</param>
  /// <param name="maxSeasons">The maximum number of seasons, or null.</param>
  /// <param name="getSeasons">Looks up a show's seasons by TMDB id.</param>
  /// <param name="logger">The logger.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The shows in range.</returns>
  public static async Task<IReadOnlyList<CatalogItem>> ApplyAsync(
    IReadOnlyList<CatalogItem> items,
    int? minSeasons,
    int? maxSeasons,
    Func<int, CancellationToken, Task<IReadOnlyList<Season>>> getSeasons,
    ILogger logger,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(items);
    ArgumentNullException.ThrowIfNull(getSeasons);
    ArgumentNullException.ThrowIfNull(logger);

    if (!IsActive(minSeasons, maxSeasons) || items.Count == 0)
    {
      return items;
    }

    var keep = new bool[items.Count];
    var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelLookups, CancellationToken = cancellationToken };
    await Parallel.ForEachAsync(Enumerable.Range(0, items.Count), options, async (i, ct) =>
    {
      try
      {
        var seasons = await getSeasons(items[i].TmdbId, ct).ConfigureAwait(false);
        keep[i] = InRange(CountSeasons(seasons), minSeasons, maxSeasons);
      }
      catch (HttpRequestException ex)
      {
        logger.LogWarning(ex, "Season lookup failed for TMDB show {TmdbId}; left out of the season filter", items[i].TmdbId);
      }
    }).ConfigureAwait(false);

    return items.Where((_, i) => keep[i]).ToList();
  }
}
