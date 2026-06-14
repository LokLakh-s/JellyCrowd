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
  /// Gets the episodes of a show's season (with air dates), used for per-episode requests and the calendar.
  /// </summary>
  /// <param name="tmdbId">The show's TMDB identifier.</param>
  /// <param name="seasonNumber">The season number.</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The season's episodes.</returns>
  Task<IReadOnlyList<Episode>> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, string language, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the details for a single movie or show.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The item details, or <c>null</c> if not found.</returns>
  Task<CatalogItem?> GetDetailsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken);

  /// <summary>
  /// Resolves a show's TVDB id from its TMDB id (used to add the series to Sonarr, which is TVDB-based).
  /// </summary>
  /// <param name="tmdbId">The show's TMDB identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The TVDB id, or <c>null</c> when unknown.</returns>
  Task<int?> GetTvdbIdAsync(int tmdbId, CancellationToken cancellationToken);

  /// <summary>
  /// Gets upcoming releases for a media type (movies via <c>movie/upcoming</c>, shows via
  /// <c>tv/on_the_air</c>), used by the releases calendar.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="region">The ISO 3166-1 region (movies).</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The upcoming catalog items.</returns>
  Task<IReadOnlyList<CatalogItem>> GetUpcomingAsync(string mediaType, string region, string language, CancellationToken cancellationToken);

  /// <summary>
  /// Lists releases of a media type within a date range (movies by primary release date, shows by
  /// first-air date), most popular first — used by the monthly releases calendar.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="fromDate">Range start (inclusive, <c>yyyy-MM-dd</c>).</param>
  /// <param name="toDate">Range end (inclusive, <c>yyyy-MM-dd</c>).</param>
  /// <param name="region">The ISO 3166-1 region (movies).</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="originalLanguage">Optional original-language filter (ISO 639-1).</param>
  /// <param name="originCountry">Optional origin-country filter (ISO 3166-1).</param>
  /// <param name="page">Result page (1-based).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The releases in the range.</returns>
  Task<IReadOnlyList<CatalogItem>> GetReleasesAsync(string mediaType, string fromDate, string toDate, string region, string language, string? originalLanguage, string? originCountry, int page, CancellationToken cancellationToken);

  /// <summary>
  /// Gets TMDB recommendations for a title (used to build the personalized "For you" row).
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The seed title's TMDB id.</param>
  /// <param name="language">The TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The recommended items.</returns>
  Task<IReadOnlyList<CatalogItem>> GetRecommendationsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken);
}
