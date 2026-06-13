using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure helper for the releases calendar: keeps items with a parseable release date that is today or
/// later, ordered chronologically (soonest first).
/// </summary>
public static class CalendarPlanner
{
  /// <summary>
  /// Filters to upcoming items (release date on or after <paramref name="todayUtc"/>) and orders them
  /// by release date ascending.
  /// </summary>
  /// <param name="items">The candidate items (movies and shows combined).</param>
  /// <param name="todayUtc">The current UTC time (only the date part is used).</param>
  /// <returns>The upcoming items, soonest first.</returns>
  public static IReadOnlyList<CatalogItem> OrderUpcoming(IEnumerable<CatalogItem> items, DateTime todayUtc)
  {
    ArgumentNullException.ThrowIfNull(items);

    var today = todayUtc.Date;
    return items
      .Select(item => (Item: item, Date: RequestScheduling.ParseReleaseDate(item.ReleaseDate)))
      .Where(pair => pair.Date is not null && pair.Date.Value.Date >= today)
      .OrderBy(pair => pair.Date!.Value)
      .Select(pair => pair.Item)
      .ToList();
  }
}
