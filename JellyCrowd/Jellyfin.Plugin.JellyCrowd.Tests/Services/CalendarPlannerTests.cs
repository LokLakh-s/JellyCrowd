using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="CalendarPlanner"/>.
/// </summary>
public class CalendarPlannerTests
{
  private static readonly DateTime Today = new(2026, 6, 13, 8, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void OrderUpcoming_KeepsTodayAndFuture_OrderedAscending()
  {
    var items = new List<CatalogItem>
    {
      new() { TmdbId = 1, Title = "Future", ReleaseDate = "2026-12-25" },
      new() { TmdbId = 2, Title = "Today", ReleaseDate = "2026-06-13" },
      new() { TmdbId = 3, Title = "Past", ReleaseDate = "2026-01-01" },
      new() { TmdbId = 4, Title = "NoDate", ReleaseDate = null }
    };

    var result = CalendarPlanner.OrderUpcoming(items, Today);

    Assert.Equal(new[] { "Today", "Future" }, result.Select(i => i.Title).ToArray());
  }

  [Fact]
  public void OrderUpcoming_Empty_ReturnsEmpty()
  {
    Assert.Empty(CalendarPlanner.OrderUpcoming(new List<CatalogItem>(), Today));
  }
}
