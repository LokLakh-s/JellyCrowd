using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;

namespace Jellyfin.Plugin.JellyCrowd.Tests;

/// <summary>
/// Minimal <see cref="ITmdbClient"/> stub for tests that don't exercise TMDB. Everything returns empty
/// except <see cref="Details"/>, which is returned by <see cref="GetDetailsAsync"/> (used for the
/// genre-based auto-approval path), and <see cref="Seasons"/>, returned by <see cref="GetSeasonsAsync"/>
/// (used to size a season/series request against the quota).
/// </summary>
internal sealed class StubTmdbClient : ITmdbClient
{
  public CatalogItem? Details { get; set; }

  public IReadOnlyList<Season> Seasons { get; set; } = Array.Empty<Season>();

  public Task<IReadOnlyList<CatalogItem>> GetTrendingAsync(string language, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

  public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, string language, int page, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

  public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string mediaType, DiscoverQuery query, string language, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

  public Task<IReadOnlyList<Genre>> GetGenresAsync(string mediaType, string language, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<Genre>>(Array.Empty<Genre>());

  public Task<IReadOnlyList<WatchProvider>> GetWatchProvidersAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<WatchProvider>>(Array.Empty<WatchProvider>());

  public Task<IReadOnlyList<Season>> GetSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken)
    => Task.FromResult(Seasons);

  public Dictionary<int, IReadOnlyList<Episode>> EpisodesBySeason { get; } = new();

  public Task<IReadOnlyList<Episode>> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, string language, CancellationToken cancellationToken)
    => Task.FromResult(EpisodesBySeason.TryGetValue(seasonNumber, out var episodes) ? episodes : (IReadOnlyList<Episode>)Array.Empty<Episode>());

  public Task<CatalogItem?> GetDetailsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
    => Task.FromResult(Details);

  public Task<IReadOnlyList<CatalogItem>> GetCollectionAsync(int collectionId, string language, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

  public Task<int?> FindTopPersonAsync(string query, string language, CancellationToken cancellationToken)
      => Task.FromResult<int?>(null);

    public Task<IReadOnlyList<CatalogItem>> GetPersonFilmographyAsync(int personId, string language, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<CatalogItem>>(new List<CatalogItem>());

    public Task<int?> GetTvdbIdAsync(int tmdbId, CancellationToken cancellationToken)
    => Task.FromResult<int?>(null);

  public Task<IReadOnlyList<CatalogItem>> GetUpcomingAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

  public Task<IReadOnlyList<CatalogItem>> GetReleasesAsync(string mediaType, string fromDate, string toDate, string region, string language, string? originalLanguage, string? originCountry, int page, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

  public Task<IReadOnlyList<CatalogItem>> GetRecommendationsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());
}
