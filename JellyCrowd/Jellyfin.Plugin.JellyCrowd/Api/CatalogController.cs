using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Exposes the TMDB-powered discovery catalog to authenticated Jellyfin users.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/Catalog")]
[Produces(MediaTypeNames.Application.Json)]
public class CatalogController : ControllerBase
{
  private const string DefaultLanguage = "en-US";

  private readonly ITmdbClient _tmdbClient;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly ILogger<CatalogController> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="CatalogController"/> class.
  /// </summary>
  /// <param name="tmdbClient">The TMDB client.</param>
  /// <param name="libraryMatcher">The library matcher used to flag already-available titles.</param>
  /// <param name="logger">The logger.</param>
  public CatalogController(ITmdbClient tmdbClient, ILibraryMatcher libraryMatcher, ILogger<CatalogController> logger)
  {
    _tmdbClient = tmdbClient;
    _libraryMatcher = libraryMatcher;
    _logger = logger;
  }

  /// <summary>
  /// Gets the catalog items trending this week.
  /// </summary>
  /// <param name="language">Optional TMDB language code (defaults to <c>en-US</c>). Pass the active Jellyfin language.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">Trending items returned.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The trending catalog items.</returns>
  [HttpGet("Trending")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<CatalogItem>>> GetTrending(
    [FromQuery] string? language,
    CancellationToken cancellationToken)
  {
    return await ExecuteAsync(
      () => _tmdbClient.GetTrendingAsync(Normalize(language), cancellationToken)).ConfigureAwait(false);
  }

  /// <summary>
  /// Searches the catalog for movies and shows matching a query.
  /// </summary>
  /// <param name="query">The free-text search query.</param>
  /// <param name="language">Optional TMDB language code (defaults to <c>en-US</c>).</param>
  /// <param name="page">Result page (1-based).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">Matching items returned.</response>
  /// <response code="400">The query was empty.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The matching catalog items.</returns>
  [HttpGet("Search")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<CatalogItem>>> Search(
    [FromQuery] string? query,
    [FromQuery] string? language,
    [FromQuery] int? page,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(query))
    {
      return BadRequest("The 'query' parameter is required.");
    }

    return await ExecuteAsync(
      () => _tmdbClient.SearchAsync(query, Normalize(language), page ?? 1, cancellationToken)).ConfigureAwait(false);
  }

  /// <summary>
  /// Gets the details for a single movie or show.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <param name="language">Optional TMDB language code (defaults to <c>en-US</c>).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">Item details returned.</response>
  /// <response code="400">The media type was invalid.</response>
  /// <response code="404">No item was found.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The item details.</returns>
  [HttpGet("Details/{mediaType}/{tmdbId:int}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<CatalogItem>> GetDetails(
    string mediaType,
    int tmdbId,
    [FromQuery] string? language,
    CancellationToken cancellationToken)
  {
    if (!string.Equals(mediaType, "movie", StringComparison.Ordinal)
        && !string.Equals(mediaType, "tv", StringComparison.Ordinal))
    {
      return BadRequest("The 'mediaType' must be 'movie' or 'tv'.");
    }

    try
    {
      var item = await _tmdbClient.GetDetailsAsync(mediaType, tmdbId, Normalize(language), cancellationToken).ConfigureAwait(false);
      if (item is null)
      {
        return NotFound();
      }

      item.JellyfinItemId = _libraryMatcher.FindItemId(item.MediaType, item.TmdbId);
      item.Available = item.JellyfinItemId is not null;
      return Ok(item);
    }
    catch (InvalidOperationException ex)
    {
      return NotConfigured(ex);
    }
    catch (HttpRequestException ex)
    {
      return Upstream(ex);
    }
  }

  /// <summary>
  /// Discovers movies or shows matching genre/year/rating filters.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>); defaults to movie.</param>
  /// <param name="genres">Comma-separated TMDB genre ids.</param>
  /// <param name="minYear">Earliest release/air year.</param>
  /// <param name="maxYear">Latest release/air year.</param>
  /// <param name="minRating">Minimum TMDB rating (0-10).</param>
  /// <param name="maxRating">Maximum TMDB rating (0-10).</param>
  /// <param name="sortBy">Sort order: <c>rating</c>, <c>release</c> or <c>popularity</c>.</param>
  /// <param name="page">Result page (1-based).</param>
  /// <param name="watchProviders">Comma-separated TMDB watch-provider ids (requires a region).</param>
  /// <param name="watchRegion">ISO 3166-1 region for watch-provider filtering (e.g. FR, US).</param>
  /// <param name="language">Optional TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">Matching items returned.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The discovered catalog items.</returns>
  [HttpGet("Discover")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<CatalogItem>>> Discover(
    [FromQuery] string? mediaType,
    [FromQuery] string? genres,
    [FromQuery] int? minYear,
    [FromQuery] int? maxYear,
    [FromQuery] double? minRating,
    [FromQuery] double? maxRating,
    [FromQuery] string? sortBy,
    [FromQuery] int? page,
    [FromQuery] string? watchProviders,
    [FromQuery] string? watchRegion,
    [FromQuery] string? language,
    CancellationToken cancellationToken)
  {
    var type = string.Equals(mediaType, "tv", StringComparison.Ordinal) ? "tv" : "movie";
    var query = new DiscoverQuery
    {
      Genres = genres,
      MinYear = minYear,
      MaxYear = maxYear,
      MinRating = minRating,
      MaxRating = maxRating,
      SortBy = sortBy,
      Page = page,
      WatchProviders = watchProviders,
      WatchRegion = watchRegion
    };

    return await ExecuteAsync(
      () => _tmdbClient.DiscoverAsync(type, query, Normalize(language), cancellationToken)).ConfigureAwait(false);
  }

  /// <summary>
  /// Lists the available genres for a media type.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="language">Optional TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The genres.</response>
  /// <response code="400">The media type was invalid.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The available genres.</returns>
  [HttpGet("Genres/{mediaType}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<Genre>>> Genres(
    string mediaType,
    [FromQuery] string? language,
    CancellationToken cancellationToken)
  {
    if (!string.Equals(mediaType, "movie", StringComparison.Ordinal)
        && !string.Equals(mediaType, "tv", StringComparison.Ordinal))
    {
      return BadRequest("The 'mediaType' must be 'movie' or 'tv'.");
    }

    try
    {
      var genres = await _tmdbClient.GetGenresAsync(mediaType, Normalize(language), cancellationToken).ConfigureAwait(false);
      return Ok(genres);
    }
    catch (InvalidOperationException ex)
    {
      return NotConfigured(ex);
    }
    catch (HttpRequestException ex)
    {
      return Upstream(ex);
    }
  }

  /// <summary>
  /// Lists the seasons of a show.
  /// </summary>
  /// <param name="tmdbId">The show's TMDB identifier.</param>
  /// <param name="language">Optional TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The seasons.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The show's seasons.</returns>
  [HttpGet("Seasons/{tmdbId:int}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<Season>>> Seasons(
    int tmdbId,
    [FromQuery] string? language,
    CancellationToken cancellationToken)
  {
    try
    {
      var seasons = await _tmdbClient.GetSeasonsAsync(tmdbId, Normalize(language), cancellationToken).ConfigureAwait(false);
      return Ok(seasons);
    }
    catch (InvalidOperationException ex)
    {
      return NotConfigured(ex);
    }
    catch (HttpRequestException ex)
    {
      return Upstream(ex);
    }
  }

  /// <summary>
  /// Lists the watch providers (streaming platforms) available in a region.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="region">ISO 3166-1 region (defaults to <c>US</c>).</param>
  /// <param name="language">Optional TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The providers.</response>
  /// <response code="400">The media type was invalid.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The available watch providers.</returns>
  [HttpGet("Providers/{mediaType}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<WatchProvider>>> Providers(
    string mediaType,
    [FromQuery] string? region,
    [FromQuery] string? language,
    CancellationToken cancellationToken)
  {
    if (!string.Equals(mediaType, "movie", StringComparison.Ordinal)
        && !string.Equals(mediaType, "tv", StringComparison.Ordinal))
    {
      return BadRequest("The 'mediaType' must be 'movie' or 'tv'.");
    }

    var watchRegion = string.IsNullOrWhiteSpace(region) ? "US" : region;

    try
    {
      var providers = await _tmdbClient.GetWatchProvidersAsync(mediaType, watchRegion, Normalize(language), cancellationToken).ConfigureAwait(false);
      return Ok(providers);
    }
    catch (InvalidOperationException ex)
    {
      return NotConfigured(ex);
    }
    catch (HttpRequestException ex)
    {
      return Upstream(ex);
    }
  }

  private static string Normalize(string? language)
    => string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language;

  private async Task<ActionResult<IReadOnlyList<CatalogItem>>> ExecuteAsync(
    Func<Task<IReadOnlyList<CatalogItem>>> action)
  {
    try
    {
      var items = await action().ConfigureAwait(false);
      foreach (var item in items)
      {
        item.JellyfinItemId = _libraryMatcher.FindItemId(item.MediaType, item.TmdbId);
        item.Available = item.JellyfinItemId is not null;
      }

      return Ok(items);
    }
    catch (InvalidOperationException ex)
    {
      return NotConfigured(ex);
    }
    catch (HttpRequestException ex)
    {
      return Upstream(ex);
    }
  }

  private ObjectResult NotConfigured(Exception ex)
  {
    _logger.LogWarning(ex, "TMDB is not configured");
    return Problem(
      detail: "The TMDB API key is not configured. Set it in the Jelly Crowd plugin settings.",
      statusCode: StatusCodes.Status503ServiceUnavailable);
  }

  private ObjectResult Upstream(Exception ex)
  {
    _logger.LogError(ex, "TMDB request failed");
    return Problem(
      detail: "The TMDB service is currently unreachable.",
      statusCode: StatusCodes.Status503ServiceUnavailable);
  }
}
