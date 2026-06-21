using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class CatalogController : ControllerBase
{
  private const string DefaultLanguage = "en-US";

  private const int MaxFollowedShows = 40;
  private const int MaxRecommendationSeeds = 8;
  private const int MaxRecommendations = 20;

  // Regions whose release dates feed the calendar, in addition to the caller's own region.
  private static readonly string[] ExtraCalendarRegions = { "FR", "ES", "IT", "GB", "US" };

  private readonly ITmdbClient _tmdbClient;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly IRequestStore _requestStore;
  private readonly IWatchlistStore _watchlistStore;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly ILogger<CatalogController> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="CatalogController"/> class.
  /// </summary>
  /// <param name="tmdbClient">The TMDB client.</param>
  /// <param name="libraryMatcher">The library matcher used to flag already-available titles.</param>
  /// <param name="requestStore">The request store, used to resolve the user's followed shows / recommendation seeds.</param>
  /// <param name="watchlistStore">The watchlist store, used as recommendation seeds.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="logger">The logger.</param>
  public CatalogController(
    ITmdbClient tmdbClient,
    ILibraryMatcher libraryMatcher,
    IRequestStore requestStore,
    IWatchlistStore watchlistStore,
    ICurrentUserAccessor userAccessor,
    ILogger<CatalogController> logger)
  {
    _tmdbClient = tmdbClient;
    _libraryMatcher = libraryMatcher;
    _requestStore = requestStore;
    _watchlistStore = watchlistStore;
    _userAccessor = userAccessor;
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
  /// Gets the movies of a TMDB collection (saga/franchise), each flagged for availability.
  /// </summary>
  /// <param name="collectionId">The TMDB collection id.</param>
  /// <param name="language">Optional TMDB language code (defaults to <c>en-US</c>).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The collection's movies.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The movies belonging to the collection, with availability flags.</returns>
  [HttpGet("Collection/{collectionId:int}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<CatalogItem>>> GetCollection(
    int collectionId,
    [FromQuery] string? language,
    CancellationToken cancellationToken)
  {
    return await ExecuteAsync(
      () => _tmdbClient.GetCollectionAsync(collectionId, Normalize(language), cancellationToken)).ConfigureAwait(false);
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
  /// <param name="originalLanguage">Optional original-language filter (ISO 639-1, e.g. fr, es).</param>
  /// <param name="originCountry">Optional production/origin-country filter (ISO 3166-1, e.g. FR, JP).</param>
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
    [FromQuery] string? originalLanguage,
    [FromQuery] string? originCountry,
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
      WatchRegion = watchRegion,
      OriginalLanguage = originalLanguage,
      OriginCountry = originCountry
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
  /// Lists the episodes of a show's season (with air dates), for per-episode requests.
  /// </summary>
  /// <param name="tmdbId">The show's TMDB identifier.</param>
  /// <param name="season">The season number.</param>
  /// <param name="language">Optional TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The episodes.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The season's episodes.</returns>
  [HttpGet("Episodes/{tmdbId:int}/{season:int}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<Episode>>> Episodes(
    int tmdbId,
    int season,
    [FromQuery] string? language,
    CancellationToken cancellationToken)
  {
    try
    {
      var episodes = await _tmdbClient.GetSeasonEpisodesAsync(tmdbId, season, Normalize(language), cancellationToken).ConfigureAwait(false);
      return Ok(episodes);
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

  /// <summary>
  /// Lists movie and show releases for the releases calendar. With <paramref name="from"/> and
  /// <paramref name="to"/> set, returns every release in that date range (monthly view); otherwise
  /// returns upcoming releases.
  /// </summary>
  /// <param name="language">Optional TMDB language code (defaults to <c>en-US</c>).</param>
  /// <param name="region">ISO 3166-1 region for movie releases (defaults to <c>US</c>).</param>
  /// <param name="from">Optional range start (<c>yyyy-MM-dd</c>).</param>
  /// <param name="to">Optional range end (<c>yyyy-MM-dd</c>).</param>
  /// <param name="originalLanguage">Optional original-language filter (ISO 639-1).</param>
  /// <param name="originCountry">Optional production/origin-country filter (ISO 3166-1).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">Releases returned.</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The catalog items, ordered by release date.</returns>
  [HttpGet("Calendar")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<CatalogItem>>> Calendar(
    [FromQuery] string? language,
    [FromQuery] string? region,
    [FromQuery] string? from,
    [FromQuery] string? to,
    [FromQuery] string? originalLanguage,
    [FromQuery] string? originCountry,
    CancellationToken cancellationToken)
  {
    var lang = Normalize(language);
    var watchRegion = string.IsNullOrWhiteSpace(region) ? "US" : region;
    var useRange = !string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to);
    var hasFilter = !string.IsNullOrWhiteSpace(originalLanguage) || !string.IsNullOrWhiteSpace(originCountry);

    try
    {
      IReadOnlyList<CatalogItem> ordered;
      if (useRange)
      {
        // Movie releases in range, across several regions (release dates are region-specific), so the
        // calendar isn't limited to one country. Deduplicated downstream by CalendarPlanner.OrderByDate.
        var items = new List<CatalogItem>();
        foreach (var regionCode in CalendarRegions(watchRegion))
        {
          var batch = await _tmdbClient.GetReleasesAsync("movie", from!, to!, regionCode, lang, originalLanguage, originCountry, 1, cancellationToken).ConfigureAwait(false);
          items.AddRange(batch);
        }

        // ...plus episodes of the user's followed shows airing in the range (unless a language/country
        // filter is active — followed-show episodes don't carry that metadata).
        if (!hasFilter)
        {
          items.AddRange(await BuildFollowedEpisodesAsync(from!, to!, lang, cancellationToken).ConfigureAwait(false));
        }

        ordered = CalendarPlanner.OrderByDate(items);
      }
      else
      {
        var movies = await _tmdbClient.GetUpcomingAsync("movie", watchRegion, lang, cancellationToken).ConfigureAwait(false);
        var shows = await _tmdbClient.GetUpcomingAsync("tv", watchRegion, lang, cancellationToken).ConfigureAwait(false);
        var merged = new List<CatalogItem>(movies);
        merged.AddRange(shows);
        ordered = CalendarPlanner.OrderUpcoming(merged, DateTime.UtcNow);
      }

      ordered = await ApplyRequestedMarkersAsync(ordered, useRange ? from : null, useRange ? to : null, cancellationToken).ConfigureAwait(false);

      foreach (var item in ordered)
      {
        item.JellyfinItemId = _libraryMatcher.FindItemId(item.MediaType, item.TmdbId);
        item.Available = item.JellyfinItemId is not null;
      }

      return Ok(ordered);
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
  /// Personalized "For you" recommendations, seeded from the user's requests and watchlist (TMDB
  /// recommendations), excluding titles they already requested or follow.
  /// </summary>
  /// <param name="language">Optional TMDB language code.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The recommended items (possibly empty when there are no seeds).</response>
  /// <response code="503">TMDB is not configured or unreachable.</response>
  /// <returns>The recommended catalog items.</returns>
  [HttpGet("Recommendations")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<IReadOnlyList<CatalogItem>>> Recommendations(
    [FromQuery] string? language,
    CancellationToken cancellationToken)
  {
    var lang = Normalize(language);

    try
    {
      var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
      var requests = await _requestStore.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
      var watchlist = await _watchlistStore.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);

      // Seeds (newest first): watchlist then requests, de-duplicated. Everything the user already has
      // or follows goes into the exclude set so it isn't recommended back.
      var exclude = new HashSet<string>(StringComparer.Ordinal);
      var seeds = new List<(string MediaType, int TmdbId)>();
      foreach (var key in watchlist.Select(w => (w.MediaType, w.TmdbId)).Concat(requests.Select(r => (r.MediaType, r.TmdbId))))
      {
        if (!IsSeedType(key.MediaType))
        {
          continue;
        }

        if (exclude.Add(RecommendationAggregator.Key(key.MediaType, key.TmdbId)))
        {
          seeds.Add(key);
        }
      }

      var candidates = new List<CatalogItem>();
      foreach (var seed in seeds.Take(MaxRecommendationSeeds))
      {
        candidates.AddRange(await _tmdbClient.GetRecommendationsAsync(seed.MediaType, seed.TmdbId, lang, cancellationToken).ConfigureAwait(false));
      }

      var recommendations = RecommendationAggregator.Aggregate(candidates, exclude, MaxRecommendations);
      foreach (var item in recommendations)
      {
        item.JellyfinItemId = _libraryMatcher.FindItemId(item.MediaType, item.TmdbId);
        item.Available = item.JellyfinItemId is not null;
      }

      return Ok(recommendations);
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

  private static bool IsSeedType(string mediaType)
    => string.Equals(mediaType, "movie", StringComparison.Ordinal)
       || string.Equals(mediaType, "tv", StringComparison.Ordinal);

  // Marks calendar items that have an active request from any user (so the UI can colour them), and —
  // when a date range is given — adds entries for requests scheduled at a future desired date. No
  // requester identity is exposed; the flag/entry is the aggregate "requested by someone".
  private async Task<IReadOnlyList<CatalogItem>> ApplyRequestedMarkersAsync(IReadOnlyList<CatalogItem> items, string? from, string? to, CancellationToken cancellationToken)
  {
    var all = await _requestStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var active = all
      .Where(r => r.Status is RequestStatus.Pending or RequestStatus.Approved)
      .ToList();

    var keys = new HashSet<string>(
      active.Select(r => r.MediaType + ":" + r.TmdbId.ToString(CultureInfo.InvariantCulture)),
      StringComparer.Ordinal);

    var list = items.ToList();
    foreach (var item in list)
    {
      // Idempotent (set true OR false) so a cached TMDB item never keeps a stale flag.
      item.Requested = keys.Contains(item.MediaType + ":" + item.TmdbId.ToString(CultureInfo.InvariantCulture));
    }

    var fromDate = from is null ? null : RequestScheduling.ParseReleaseDate(from);
    var toDate = to is null ? null : RequestScheduling.ParseReleaseDate(to);
    if (fromDate is null || toDate is null)
    {
      return list;
    }

    var existing = new HashSet<string>(
      list.Select(i => i.MediaType + ":" + i.TmdbId.ToString(CultureInfo.InvariantCulture) + ":" + (i.ReleaseDate ?? string.Empty)),
      StringComparer.Ordinal);
    var today = DateTime.UtcNow.Date;
    var added = false;
    foreach (var request in active)
    {
      if (request.DesiredAt is not { } desired)
      {
        continue;
      }

      var day = desired.Date;
      if (day <= today || day < fromDate.Value.Date || day > toDate.Value.Date)
      {
        continue;
      }

      var iso = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      if (!existing.Add(request.MediaType + ":" + request.TmdbId.ToString(CultureInfo.InvariantCulture) + ":" + iso))
      {
        continue;
      }

      list.Add(new CatalogItem
      {
        TmdbId = request.TmdbId,
        MediaType = request.MediaType,
        Title = request.Title,
        PosterPath = request.PosterPath,
        ReleaseDate = iso,
        Requested = true
      });
      added = true;
    }

    return added ? CalendarPlanner.OrderByDate(list) : list;
  }

  // Episodes airing in the date range for the shows the user cares about: shows they have requested
  // AND shows on their watchlist ("séries suivies"). TMDB has no global episode calendar, so we fetch
  // per show; bounded to MaxFollowedShows and to each show's latest season (best for the current months).
  private async Task<IReadOnlyList<CatalogItem>> BuildFollowedEpisodesAsync(string from, string to, string language, CancellationToken cancellationToken)
  {
    var result = new List<CatalogItem>();
    var fromDate = RequestScheduling.ParseReleaseDate(from);
    var toDate = RequestScheduling.ParseReleaseDate(to);
    if (fromDate is null || toDate is null)
    {
      return result;
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var requests = await _requestStore.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    var watchlist = await _watchlistStore.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);

    // Merge requested + watchlisted TV shows, de-duplicated by TMDB id (first occurrence wins for the
    // display title/poster), bounded to keep the per-show TMDB fan-out in check.
    var shows = requests
      .Where(r => string.Equals(r.MediaType, "tv", StringComparison.Ordinal))
      .Select(r => new { r.TmdbId, r.Title, r.PosterPath })
      .Concat(watchlist
        .Where(w => string.Equals(w.MediaType, "tv", StringComparison.Ordinal))
        .Select(w => new { w.TmdbId, w.Title, w.PosterPath }))
      .GroupBy(s => s.TmdbId)
      .Select(g => g.First())
      .Take(MaxFollowedShows)
      .ToList();

    foreach (var show in shows)
    {
      try
      {
        var seasons = await _tmdbClient.GetSeasonsAsync(show.TmdbId, language, cancellationToken).ConfigureAwait(false);
        var latest = seasons.Where(s => s.SeasonNumber > 0).OrderByDescending(s => s.SeasonNumber).FirstOrDefault();
        if (latest is null)
        {
          continue;
        }

        var episodes = await _tmdbClient.GetSeasonEpisodesAsync(show.TmdbId, latest.SeasonNumber, language, cancellationToken).ConfigureAwait(false);
        foreach (var episode in episodes)
        {
          var air = RequestScheduling.ParseReleaseDate(episode.AirDate);
          if (air is null || air.Value.Date < fromDate.Value.Date || air.Value.Date > toDate.Value.Date)
          {
            continue;
          }

          result.Add(new CatalogItem
          {
            TmdbId = show.TmdbId,
            MediaType = "tv",
            Title = show.Title,
            PosterPath = show.PosterPath,
            ReleaseDate = episode.AirDate,
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            EpisodeName = episode.Name
          });
        }
      }
      catch (HttpRequestException ex)
      {
        _logger.LogDebug(ex, "Calendar: could not load episodes for show {TmdbId}", show.TmdbId);
      }
    }

    return result;
  }

  // The caller's region first, then the extra calendar regions, de-duplicated.
  private static IEnumerable<string> CalendarRegions(string primaryRegion)
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(primaryRegion) && seen.Add(primaryRegion))
    {
      yield return primaryRegion;
    }

    foreach (var region in ExtraCalendarRegions)
    {
      if (seen.Add(region))
      {
        yield return region;
      }
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
