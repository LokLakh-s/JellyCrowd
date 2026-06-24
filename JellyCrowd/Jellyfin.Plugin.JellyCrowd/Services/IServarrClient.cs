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
  /// Updates an existing series (<c>PUT /api/v3/series/{id}</c>), e.g. to monitor an additional season.
  /// </summary>
  /// <param name="baseUrl">The Sonarr base URL.</param>
  /// <param name="apiKey">The Sonarr API key.</param>
  /// <param name="seriesId">The Sonarr series id.</param>
  /// <param name="body">The full series body to persist.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the series is updated.</returns>
  Task UpdateSeriesAsync(string baseUrl, string apiKey, int seriesId, JsonObject body, CancellationToken cancellationToken);

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
  /// Fetches the configured indexers (<c>GET /api/v3/indexer</c>), used by diagnostics to confirm at
  /// least one search source is enabled.
  /// </summary>
  /// <param name="baseUrl">The instance base URL.</param>
  /// <param name="apiKey">The API key.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The raw indexers JSON array.</returns>
  Task<string> GetIndexersAsync(string baseUrl, string apiKey, CancellationToken cancellationToken);

  /// <summary>
  /// Triggers a command (<c>POST /api/v3/command</c>), e.g. <c>MoviesSearch</c>, <c>SeriesSearch</c>
  /// or <c>SeasonSearch</c>, to re-run a release search for an already-added item.
  /// </summary>
  /// <param name="baseUrl">The instance base URL.</param>
  /// <param name="apiKey">The API key.</param>
  /// <param name="body">The command body (must include a <c>name</c>).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the command is accepted.</returns>
  Task CommandAsync(string baseUrl, string apiKey, JsonObject body, CancellationToken cancellationToken);

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

  /// <summary>
  /// Removes a series from Sonarr (<c>DELETE /api/v3/series/{id}</c>), stopping its search/download.
  /// </summary>
  /// <param name="baseUrl">The Sonarr base URL.</param>
  /// <param name="apiKey">The Sonarr API key.</param>
  /// <param name="seriesId">The Sonarr series id.</param>
  /// <param name="deleteFiles">Whether to also delete any downloaded files.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the series is removed.</returns>
  Task DeleteSeriesAsync(string baseUrl, string apiKey, int seriesId, bool deleteFiles, CancellationToken cancellationToken);

  /// <summary>
  /// Removes a queue item (<c>DELETE /api/v3/queue/{id}</c>), optionally also removing the active
  /// download from the download client — needed so a cancel actually stops the grab (e.g. in RDT).
  /// </summary>
  /// <param name="baseUrl">The Radarr/Sonarr base URL.</param>
  /// <param name="apiKey">The API key.</param>
  /// <param name="queueItemId">The queue record id.</param>
  /// <param name="removeFromClient">Whether to also remove the download from the download client.</param>
  /// <param name="blocklist">Whether to blocklist the release.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the queue item is removed.</returns>
  Task DeleteQueueItemAsync(string baseUrl, string apiKey, int queueItemId, bool removeFromClient, bool blocklist, CancellationToken cancellationToken);

  /// <summary>
  /// Gets Prowlarr's raw indexers JSON (<c>GET /api/v1/indexer</c>) for the Diagnostics indexer check.
  /// Prowlarr is the upstream indexer manager, on a different API version than Radarr/Sonarr.
  /// </summary>
  /// <param name="baseUrl">The Prowlarr base URL.</param>
  /// <param name="apiKey">The Prowlarr API key.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The raw indexers JSON.</returns>
  Task<string> GetProwlarrIndexersAsync(string baseUrl, string apiKey, CancellationToken cancellationToken);
}
