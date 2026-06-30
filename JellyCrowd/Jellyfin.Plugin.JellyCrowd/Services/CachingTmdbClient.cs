using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// In-memory caching decorator over <see cref="ITmdbClient"/>. TMDB catalog data changes slowly, so
/// caching read results for a short while spares the TMDB API quota and cuts latency. The cache is
/// bounded (expired entries are purged; growth past a cap is skipped rather than stored).
/// </summary>
public sealed class CachingTmdbClient : ITmdbClient
{
  private const int MaxEntries = 1000;
  // TMDB catalog data changes slowly, so cache it long enough that browsing stays instant (and TMDB is
  // hit at most ~twice a day per view) rather than going cold every few minutes. Dynamic state — a
  // title's "available"/"requested" badge — is applied fresh per request in CatalogController, after
  // this cache, so a long catalog TTL never shows stale availability.
  private static readonly TimeSpan ShortTtl = TimeSpan.FromHours(12);   // catalog lists / details
  private static readonly TimeSpan LongTtl = TimeSpan.FromHours(24);    // near-static (genres, id mapping)

  private readonly ITmdbClient _inner;
  private readonly Func<DateTime> _now;
  private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

  /// <summary>
  /// Initializes a new instance of the <see cref="CachingTmdbClient"/> class.
  /// </summary>
  /// <param name="inner">The underlying TMDB client.</param>
  /// <param name="now">Clock accessor (defaults to <see cref="DateTime.UtcNow"/>); injectable for tests.</param>
  public CachingTmdbClient(ITmdbClient inner, Func<DateTime>? now = null)
  {
    _inner = inner;
    _now = now ?? (() => DateTime.UtcNow);
  }

  /// <inheritdoc />
  public Task<IReadOnlyList<CatalogItem>> GetTrendingAsync(string language, CancellationToken cancellationToken)
    => GetOrAddAsync("trending|" + language, ShortTtl, () => _inner.GetTrendingAsync(language, cancellationToken));

  /// <inheritdoc />
  public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, string language, int page, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("search", query, language, page), ShortTtl, () => _inner.SearchAsync(query, language, page, cancellationToken));

  /// <inheritdoc />
  public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string mediaType, DiscoverQuery query, string language, CancellationToken cancellationToken)
    => GetOrAddAsync(DiscoverKey(mediaType, query, language), ShortTtl, () => _inner.DiscoverAsync(mediaType, query, language, cancellationToken));

  // Stable cache key built from the discover filters (DiscoverQuery is a plain class, so its default
  // hash is reference-based and would never hit the cache).
  private static string DiscoverKey(string mediaType, DiscoverQuery q, string language)
    => Key("discover", mediaType, language, q.Genres ?? "-", q.MinYear, q.MaxYear, q.MinRating, q.MaxRating, q.SortBy ?? "-", q.Page, q.WatchProviders ?? "-", q.WatchRegion ?? "-", q.OriginalLanguage ?? "-", q.OriginCountry ?? "-");

  /// <inheritdoc />
  public Task<IReadOnlyList<Genre>> GetGenresAsync(string mediaType, string language, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("genres", mediaType, language), LongTtl, () => _inner.GetGenresAsync(mediaType, language, cancellationToken));

  /// <inheritdoc />
  public Task<IReadOnlyList<WatchProvider>> GetWatchProvidersAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("providers", mediaType, region, language), LongTtl, () => _inner.GetWatchProvidersAsync(mediaType, region, language, cancellationToken));

  /// <inheritdoc />
  public Task<IReadOnlyList<Season>> GetSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("seasons", tmdbId, language), ShortTtl, () => _inner.GetSeasonsAsync(tmdbId, language, cancellationToken));

  /// <inheritdoc />
  public Task<IReadOnlyList<Episode>> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, string language, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("episodes", tmdbId, seasonNumber, language), ShortTtl, () => _inner.GetSeasonEpisodesAsync(tmdbId, seasonNumber, language, cancellationToken));

  /// <inheritdoc />
  public Task<CatalogItem?> GetDetailsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("details", mediaType, tmdbId, language), ShortTtl, () => _inner.GetDetailsAsync(mediaType, tmdbId, language, cancellationToken));

  /// <inheritdoc />
  public Task<IReadOnlyList<CatalogItem>> GetCollectionAsync(int collectionId, string language, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("collection", collectionId, language), LongTtl, () => _inner.GetCollectionAsync(collectionId, language, cancellationToken));

  /// <inheritdoc />
  public Task<int?> GetTvdbIdAsync(int tmdbId, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("tvdb", tmdbId), LongTtl, () => _inner.GetTvdbIdAsync(tmdbId, cancellationToken));

  /// <inheritdoc />
  public Task<IReadOnlyList<CatalogItem>> GetUpcomingAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("upcoming", mediaType, region, language), ShortTtl, () => _inner.GetUpcomingAsync(mediaType, region, language, cancellationToken));

  /// <inheritdoc />
  public Task<IReadOnlyList<CatalogItem>> GetReleasesAsync(string mediaType, string fromDate, string toDate, string region, string language, string? originalLanguage, string? originCountry, int page, CancellationToken cancellationToken)
    => GetOrAddAsync(
      Key("releases", mediaType, fromDate, toDate, region, language, originalLanguage ?? "-", originCountry ?? "-", page),
      ShortTtl,
      () => _inner.GetReleasesAsync(mediaType, fromDate, toDate, region, language, originalLanguage, originCountry, page, cancellationToken));

  /// <inheritdoc />
  public Task<IReadOnlyList<CatalogItem>> GetRecommendationsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
    => GetOrAddAsync(Key("recommend", mediaType, tmdbId, language), ShortTtl, () => _inner.GetRecommendationsAsync(mediaType, tmdbId, language, cancellationToken));

  private static string Key(params object?[] parts)
    => string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));

  private async Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
  {
    if (_cache.TryGetValue(key, out var entry) && entry.Expiry > _now())
    {
      return (T)entry.Value!;
    }

    var value = await factory().ConfigureAwait(false);
    Store(key, new CacheEntry(_now() + ttl, value));
    return value;
  }

  private void Store(string key, CacheEntry entry)
  {
    if (_cache.Count >= MaxEntries)
    {
      PurgeExpired();
      if (_cache.Count >= MaxEntries)
      {
        return; // bounded: serve fresh next time rather than grow without limit
      }
    }

    _cache[key] = entry;
  }

  private void PurgeExpired()
  {
    var now = _now();
    foreach (var pair in _cache.Where(p => p.Value.Expiry <= now).ToList())
    {
      _cache.TryRemove(pair.Key, out _);
    }
  }

  private sealed record CacheEntry(DateTime Expiry, object? Value);
}
