using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Low-level access to a Radarr/Sonarr (v3) instance: connectivity test, selectable resources,
/// lookups and add. Each call takes the instance base URL and API key so the same client serves
/// both the Radarr and Sonarr configurations.
/// </summary>
public interface IServarrClient
{
  /// <summary>
  /// Verifies connectivity/authentication (<c>GET /api/v3/system/status</c>). Throws on failure.
  /// </summary>
  /// <param name="baseUrl">The instance base URL.</param>
  /// <param name="apiKey">The API key.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the instance answered successfully.</returns>
  Task TestAsync(string baseUrl, string apiKey, CancellationToken cancellationToken);

  /// <summary>
  /// Fetches the instance's root folders and quality (and optionally language) profiles.
  /// </summary>
  /// <param name="baseUrl">The instance base URL.</param>
  /// <param name="apiKey">The API key.</param>
  /// <param name="includeLanguageProfiles">Whether to also fetch language profiles (Sonarr v3).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The selectable resources.</returns>
  Task<ServarrResources> GetResourcesAsync(string baseUrl, string apiKey, bool includeLanguageProfiles, CancellationToken cancellationToken);

  /// <summary>
  /// Looks up a movie by TMDB id (<c>GET /api/v3/movie/lookup/tmdb</c>).
  /// </summary>
  /// <param name="baseUrl">The Radarr base URL.</param>
  /// <param name="apiKey">The Radarr API key.</param>
  /// <param name="tmdbId">The TMDB id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The lookup object, or <c>null</c> when not found.</returns>
  Task<JsonObject?> LookupMovieAsync(string baseUrl, string apiKey, int tmdbId, CancellationToken cancellationToken);

  /// <summary>
  /// Looks up a series by TVDB id (<c>GET /api/v3/series/lookup?term=tvdb:{id}</c>).
  /// </summary>
  /// <param name="baseUrl">The Sonarr base URL.</param>
  /// <param name="apiKey">The Sonarr API key.</param>
  /// <param name="tvdbId">The TVDB id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The lookup object, or <c>null</c> when not found.</returns>
  Task<JsonObject?> LookupSeriesAsync(string baseUrl, string apiKey, int tvdbId, CancellationToken cancellationToken);

  /// <summary>
  /// Adds a movie (<c>POST /api/v3/movie</c>).
  /// </summary>
  /// <param name="baseUrl">The Radarr base URL.</param>
  /// <param name="apiKey">The Radarr API key.</param>
  /// <param name="body">The add-movie body.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the movie is added.</returns>
  Task AddMovieAsync(string baseUrl, string apiKey, JsonObject body, CancellationToken cancellationToken);

  /// <summary>
  /// Adds a series (<c>POST /api/v3/series</c>).
  /// </summary>
  /// <param name="baseUrl">The Sonarr base URL.</param>
  /// <param name="apiKey">The Sonarr API key.</param>
  /// <param name="body">The add-series body.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the series is added.</returns>
  Task AddSeriesAsync(string baseUrl, string apiKey, JsonObject body, CancellationToken cancellationToken);

  /// <summary>
  /// Fetches the current download queue (<c>GET /api/v3/queue</c>), including the linked movie
  /// (Radarr) or series + episode (Sonarr) so records can be matched back to a request.
  /// </summary>
  /// <param name="baseUrl">The instance base URL.</param>
  /// <param name="apiKey">The API key.</param>
  /// <param name="forSonarr"><c>true</c> for Sonarr (include series/episode), <c>false</c> for Radarr (include movie).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The raw queue JSON payload.</returns>
  Task<string> GetQueueAsync(string baseUrl, string apiKey, bool forSonarr, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the added Radarr movie for a TMDB id (<c>GET /api/v3/movie?tmdbId={id}</c>), or <c>null</c>
  /// when it is not in Radarr.
  /// </summary>
  /// <param name="baseUrl">The Radarr base URL.</param>
  /// <param name="apiKey">The Radarr API key.</param>
  /// <param name="tmdbId">The TMDB id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The movie object, or <c>null</c>.</returns>
  Task<JsonObject?> GetMovieByTmdbAsync(string baseUrl, string apiKey, int tmdbId, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the added Sonarr series for a TVDB id (<c>GET /api/v3/series?tvdbId={id}</c>), or <c>null</c>
  /// when it is not in Sonarr.
  /// </summary>
  /// <param name="baseUrl">The Sonarr base URL.</param>
  /// <param name="apiKey">The Sonarr API key.</param>
  /// <param name="tvdbId">The TVDB id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The series object, or <c>null</c>.</returns>
  Task<JsonObject?> GetSeriesByTvdbAsync(string baseUrl, string apiKey, int tvdbId, CancellationToken cancellationToken);

  /// <summary>
  /// Gets all episodes of a Sonarr series (<c>GET /api/v3/episode?seriesId={id}</c>).
  /// </summary>
  /// <param name="baseUrl">The Sonarr base URL.</param>
  /// <param name="apiKey">The Sonarr API key.</param>
  /// <param name="seriesId">The Sonarr series id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The raw episodes JSON array.</returns>
  Task<string> GetEpisodesAsync(string baseUrl, string apiKey, int seriesId, CancellationToken cancellationToken);

  /// <summary>
  /// Removes a movie from Radarr (<c>DELETE /api/v3/movie/{id}</c>), stopping its search/download.
  /// </summary>
  /// <param name="baseUrl">The Radarr base URL.</param>
  /// <param name="apiKey">The Radarr API key.</param>
  /// <param name="movieId">The Radarr movie id.</param>
  /// <param name="deleteFiles">Whether to also delete any downloaded files.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the movie is removed.</returns>
  Task DeleteMovieAsync(string baseUrl, string apiKey, int movieId, bool deleteFiles, CancellationToken cancellationToken);
}
