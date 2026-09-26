using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="SeasonRangeFilter"/>: the catalog's min/max number-of-seasons filter.
/// </summary>
public class SeasonRangeFilterTests
{
  private static IReadOnlyList<Season> Seasons(params int[] numbers)
    => numbers.Select(n => new Season { SeasonNumber = n }).ToList();

  private static List<CatalogItem> Shows(params int[] ids)
    => ids.Select(id => new CatalogItem { TmdbId = id, MediaType = "tv", Title = "S" + id }).ToList();

  [Fact]
  public void CountSeasons_LeavesSpecialsOut_LikeTmdbNumberOfSeasons()
  {
    Assert.Equal(5, SeasonRangeFilter.CountSeasons(Seasons(0, 1, 2, 3, 4, 5)));
    Assert.Equal(0, SeasonRangeFilter.CountSeasons(Seasons(0)));
  }

  [Theory]
  [InlineData(3, 2, 4, true)]
  [InlineData(2, 2, 4, true)]
  [InlineData(4, 2, 4, true)]
  [InlineData(1, 2, 4, false)]
  [InlineData(5, 2, 4, false)]
  [InlineData(40, 5, null, true)]
  [InlineData(1, null, 1, true)]
  [InlineData(2, null, 1, false)]
  public void InRange_BoundsAreInclusive_AndOptional(int count, int? min, int? max, bool expected)
  {
    Assert.Equal(expected, SeasonRangeFilter.InRange(count, min, max));
  }

  [Theory]
  [InlineData(null, null, true)]
  [InlineData(1, 20, true)]
  [InlineData(3, 3, true)]
  [InlineData(3, 2, false)]
  [InlineData(0, null, false)]
  [InlineData(null, -1, false)]
  public void IsValid_RejectsNonPositiveBoundsAndInvertedRanges(int? min, int? max, bool expected)
  {
    Assert.Equal(expected, SeasonRangeFilter.IsValid(min, max));
  }

  [Fact]
  public async Task ApplyAsync_KeepsShowsInRange_InTheirOriginalOrder()
  {
    var counts = new Dictionary<int, IReadOnlyList<Season>>
    {
      [10] = Seasons(1, 2, 3),
      [11] = Seasons(1),
      [12] = Seasons(0, 1, 2),
      [13] = Seasons(1, 2, 3, 4, 5, 6)
    };

    var kept = await SeasonRangeFilter.ApplyAsync(
      Shows(10, 11, 12, 13), 2, 3, (id, _) => Task.FromResult(counts[id]), NullLogger.Instance, CancellationToken.None);

    Assert.Equal(new[] { 10, 12 }, kept.Select(i => i.TmdbId));
  }

  [Fact]
  public async Task ApplyAsync_WithoutARange_MakesNoLookup()
  {
    var lookups = 0;
    var items = Shows(1, 2);

    var kept = await SeasonRangeFilter.ApplyAsync(
      items, null, null, (_, _) => { lookups++; return Task.FromResult(Seasons(1)); }, NullLogger.Instance, CancellationToken.None);

    Assert.Same(items, kept);
    Assert.Equal(0, lookups);
  }

  [Fact]
  public async Task ApplyAsync_AShowThatFailsToLoad_IsLeftOut_WithoutFailingThePage()
  {
    Task<IReadOnlyList<Season>> Lookup(int id, CancellationToken ct)
      => id == 2 ? throw new HttpRequestException("TMDB down") : Task.FromResult(Seasons(1));

    var kept = await SeasonRangeFilter.ApplyAsync(Shows(1, 2, 3), null, 1, Lookup, NullLogger.Instance, CancellationToken.None);

    Assert.Equal(new[] { 1, 3 }, kept.Select(i => i.TmdbId));
  }

  [Fact]
  public async Task ApplyAsync_Cancellation_Propagates()
  {
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SeasonRangeFilter.ApplyAsync(
      Shows(1), 1, null, (_, ct) => Task.FromResult(Seasons(1)), NullLogger.Instance, cts.Token));
  }
}
