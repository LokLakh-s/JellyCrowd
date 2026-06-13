using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Read access to the TMDB discovery catalog.
/// </summary>
public interface ITmdbClient
{
  /// <summary>
  /// Gets the items trending this week (movies and shows).
  /// </summary>
  /// <param name="language">The TMDB language code (e.g. <c>en-US</c>, <c>fr-FR</c>).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The trending catalog items.</returns>
  Task<IReadOnlyList<CatalogItem>> GetTrendingAsync(string language, CancellationToken cancellationToken);

  /// <summary>
  /// Searches movies and shows matching the given query.
  /// </summary>
  /// <param name="query">The free-text search query.</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="page">The result page (1-based).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The matching catalog items.</returns>
  Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, string language, int page, CancellationToken cancellationToken);

  /// <summary>
  /// Discovers movies or shows matching the given filters.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="query">The discover filters.</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The matching catalog items.</returns>
  Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string mediaType, DiscoverQuery query, string language, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the available genres for a media type.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The genres.</returns>
  Task<IReadOnlyList<Genre>> GetGenresAsync(string mediaType, string language, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the watch providers (streaming platforms) available in a region for a media type.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="region">The ISO 3166-1 region (e.g. <c>FR</c>).</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The providers, most prominent first.</returns>
  Task<IReadOnlyList<WatchProvider>> GetWatchProvidersAsync(string mediaType, string region, string language, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the seasons of a show.
  /// </summary>
  /// <param name="tmdbId">The show's TMDB identifier.</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The show's seasons.</returns>
  Task<IReadOnlyList<Season>> GetSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the details for a single movie or show.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The item details, or <c>null</c> if not found.</returns>
  Task<CatalogItem?> GetDetailsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken);
}
