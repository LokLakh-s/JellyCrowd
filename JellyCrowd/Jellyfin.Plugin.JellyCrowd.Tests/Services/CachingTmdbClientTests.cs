using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="CachingTmdbClient"/>.
/// </summary>
public class CachingTmdbClientTests
{
  [Fact]
  public async Task GetTrending_CachesWithinTtl_RefetchesAfterExpiry()
  {
    var inner = new CountingTmdbClient();
    var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var client = new CachingTmdbClient(inner, () => now);

    await client.GetTrendingAsync("en-US", CancellationToken.None);
    await client.GetTrendingAsync("en-US", CancellationToken.None);
    Assert.Equal(1, inner.TrendingCalls); // second served from cache

    now = now.AddMinutes(11); // past the 10-minute TTL
    await client.GetTrendingAsync("en-US", CancellationToken.None);
    Assert.Equal(2, inner.TrendingCalls);
  }

  [Fact]
  public async Task DifferentArgs_AreCachedSeparately()
  {
    var inner = new CountingTmdbClient();
    var client = new CachingTmdbClient(inner, () => DateTime.UtcNow);

    await client.GetTrendingAsync("en-US", CancellationToken.None);
    await client.GetTrendingAsync("fr-FR", CancellationToken.None);

    Assert.Equal(2, inner.TrendingCalls);
  }

  [Fact]
  public async Task Discover_KeyedByFilters_NotByInstance()
  {
    var inner = new CountingTmdbClient();
    var client = new CachingTmdbClient(inner, () => DateTime.UtcNow);

    await client.DiscoverAsync("movie", new DiscoverQuery { Genres = "28", MinYear = 2000 }, "en-US", CancellationToken.None);
    await client.DiscoverAsync("movie", new DiscoverQuery { Genres = "28", MinYear = 2000 }, "en-US", CancellationToken.None);

    Assert.Equal(1, inner.DiscoverCalls); // same filters -> cache hit despite a new query instance
  }

  private sealed class CountingTmdbClient : ITmdbClient
  {
    public int TrendingCalls { get; private set; }

    public int DiscoverCalls { get; private set; }

    public Task<IReadOnlyList<CatalogItem>> GetTrendingAsync(string language, CancellationToken cancellationToken)
    {
      TrendingCalls++;
      return Task.FromResult<IReadOnlyList<CatalogItem>>(new List<CatalogItem> { new() { TmdbId = 1 } });
    }

    public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string mediaType, DiscoverQuery query, string language, CancellationToken cancellationToken)
    {
      DiscoverCalls++;
      return Task.FromResult<IReadOnlyList<CatalogItem>>(new List<CatalogItem>());
    }

    public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, string language, int page, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<CatalogItem>>(new List<CatalogItem>());

    public Task<IReadOnlyList<Genre>> GetGenresAsync(string mediaType, string language, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<Genre>>(new List<Genre>());

    public Task<IReadOnlyList<WatchProvider>> GetWatchProvidersAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<WatchProvider>>(new List<WatchProvider>());

    public Task<IReadOnlyList<Season>> GetSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<Season>>(new List<Season>());

    public Task<IReadOnlyList<Episode>> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, string language, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<Episode>>(new List<Episode>());

    public Task<CatalogItem?> GetDetailsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
      => Task.FromResult<CatalogItem?>(null);

    public Task<int?> GetTvdbIdAsync(int tmdbId, CancellationToken cancellationToken)
      => Task.FromResult<int?>(null);

    public Task<IReadOnlyList<CatalogItem>> GetUpcomingAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<CatalogItem>>(new List<CatalogItem>());

    public Task<IReadOnlyList<CatalogItem>> GetReleasesAsync(string mediaType, string fromDate, string toDate, string region, string language, string? originalLanguage, string? originCountry, int page, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<CatalogItem>>(new List<CatalogItem>());

    public Task<IReadOnlyList<CatalogItem>> GetRecommendationsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<CatalogItem>>(new List<CatalogItem>());
  }
}
