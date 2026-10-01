using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure rules behind "remove from Continue watching": which rows of a resume or next-up result a user
/// removed, and how far the query must reach so the row still comes back full.
/// </summary>
public static class HiddenResumePolicy
{
  /// <summary>
  /// The most extra items a query is widened by, whatever the number of removals.
  /// </summary>
  public const int MaxExtra = 50;

  /// <summary>
  /// How many more items to ask for so that, once the removed ones are dropped, the row is still full.
  /// </summary>
  /// <param name="hiddenCount">The number of entries the user removed.</param>
  /// <returns>The extra items to ask for.</returns>
  public static int ExtraLimit(int hiddenCount) => Math.Clamp(hiddenCount, 0, MaxExtra);

  /// <summary>
  /// Whether an item is one the user removed: the movie itself, or any episode of a removed show.
  /// </summary>
  /// <param name="item">The item.</param>
  /// <param name="hidden">The user's removals.</param>
  /// <returns><c>true</c> when it must not be shown.</returns>
  public static bool IsHidden(BaseItemDto item, IReadOnlyCollection<HiddenResumeEntry> hidden)
  {
    ArgumentNullException.ThrowIfNull(item);
    ArgumentNullException.ThrowIfNull(hidden);
    return hidden.Any(h => h.SeriesId.HasValue
      ? item.SeriesId == h.SeriesId || item.Id == h.SeriesId
      : item.Id == h.ItemId);
  }

  /// <summary>
  /// Drops the removed items from a result, trims it back to the size the client asked for, and lowers the
  /// total accordingly.
  /// </summary>
  /// <param name="result">The resume or next-up result (changed in place).</param>
  /// <param name="hidden">The user's removals.</param>
  /// <param name="requestedLimit">The limit the client asked for, when the query was widened; otherwise <c>null</c>.</param>
  /// <returns>The number of items dropped.</returns>
  public static int Apply(QueryResult<BaseItemDto> result, IReadOnlyCollection<HiddenResumeEntry> hidden, int? requestedLimit)
  {
    ArgumentNullException.ThrowIfNull(result);
    ArgumentNullException.ThrowIfNull(hidden);
    var items = result.Items ?? Array.Empty<BaseItemDto>();
    var kept = items.Where(i => !IsHidden(i, hidden)).ToList();
    var dropped = items.Count - kept.Count;
    if (requestedLimit is int limit && limit >= 0 && kept.Count > limit)
    {
      kept = kept.Take(limit).ToList();
    }

    result.Items = kept;
    result.TotalRecordCount = Math.Max(0, result.TotalRecordCount - dropped);
    return dropped;
  }
}
