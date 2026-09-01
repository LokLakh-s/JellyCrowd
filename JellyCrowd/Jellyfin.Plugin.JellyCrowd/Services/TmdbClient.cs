using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="ITmdbClient"/> implementation backed by the TMDB v3 REST API.
/// </summary>
public class TmdbClient : ITmdbClient
{
  private const string DefaultBaseUrl = "https://api.themoviedb.org/3";

  // Cap a person's filmography: enough to cover a real career, sorted by popularity so guest spots and
  // talk-show appearances fall off the end rather than burying the notable films and shows.
  private const int PersonFilmographyMax = 80;

  private readonly IHttpClientFactory _httpClientFactory;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<TmdbClient> _logger;
  private readonly string _baseUrl;

  /// <summary>
  /// Initializes a new instance of the <see cref="TmdbClient"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory.</param>
  /// <param name="config">Accessor for the current plugin configuration (supplies the TMDB API key).</param>
  /// <param name="logger">The logger.</param>
  public TmdbClient(IHttpClientFactory httpClientFactory, Func<PluginConfiguration> config, ILogger<TmdbClient> logger)
    : this(httpClientFactory, config, logger, DefaultBaseUrl)
  {
  }

  // Test seam: overrides the TMDB base URL so integration tests can point the client at a mock server.
  internal TmdbClient(IHttpClientFactory httpClientFactory, Func<PluginConfiguration> config, ILogger<TmdbClient> logger, string baseUrl)
  {
    _httpClientFactory = httpClientFactory;
    _config = config;
    _logger = logger;
    _baseUrl = baseUrl;
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<CatalogItem>> GetTrendingAsync(string language, CancellationToken cancellationToken)
  {
    var json = await GetAsync($"/trending/all/week?language={Escape(language)}", cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseResults(json);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, string language, int page, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(query);

    var resultPage = page > 0 ? page : 1;
    var json = await GetAsync(
      $"/search/multi?query={Escape(query)}&language={Escape(language)}&include_adult=false&page={resultPage.ToString(CultureInfo.InvariantCulture)}",
      cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseResults(json);
  }

  /// <inheritdoc />
  public async Task<int?> FindTopPersonAsync(string query, string language, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(query);
    var json = await GetAsync(
      $"/search/multi?query={Escape(query)}&language={Escape(language)}&include_adult=false&page=1",
      cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseTopPersonId(json);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<CatalogItem>> GetPersonFilmographyAsync(int personId, string language, CancellationToken cancellationToken)
  {
    var json = await GetAsync(
      $"/person/{personId.ToString(CultureInfo.InvariantCulture)}/combined_credits?language={Escape(language)}",
      cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseCombinedCredits(json, PersonFilmographyMax);
  }

  /// <inheritdoc />
  public async Task<CatalogItem?> GetDetailsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
  {
    EnsureMediaType(mediaType);

    var id = tmdbId.ToString(CultureInfo.InvariantCulture);
    var json = await GetAsync($"/{mediaType}/{id}?language={Escape(language)}&append_to_response=external_ids,credits", cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseDetails(json, mediaType);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<CatalogItem>> GetCollectionAsync(int collectionId, string language, CancellationToken cancellationToken)
  {
    var json = await GetAsync($"/collection/{collectionId.ToString(CultureInfo.InvariantCulture)}?language={Escape(language)}", cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseCollectionParts(json);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string mediaType, DiscoverQuery query, string language, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(query);
    EnsureMediaType(mediaType);

    var isMovie = string.Equals(mediaType, "movie", StringComparison.Ordinal);
    var builder = new StringBuilder();
    builder.Append("/discover/").Append(mediaType)
      .Append("?language=").Append(Escape(language))
      .Append("&include_adult=false&vote_count.gte=50&sort_by=").Append(SortBy(query.SortBy, isMovie));

    if (!string.IsNullOrWhiteSpace(query.Genres))
    {
      builder.Append("&with_genres=").Append(Escape(query.Genres));
    }

    if (query.MinYear.HasValue)
    {
      builder.Append('&').Append(isMovie ? "primary_release_date.gte" : "first_air_date.gte")
        .Append('=').Append(query.MinYear.Value.ToString(CultureInfo.InvariantCulture)).Append("-01-01");
    }

    if (query.MaxYear.HasValue)
    {
      builder.Append('&').Append(isMovie ? "primary_release_date.lte" : "first_air_date.lte")
        .Append('=').Append(query.MaxYear.Value.ToString(CultureInfo.InvariantCulture)).Append("-12-31");
    }

    if (query.MinRating.HasValue)
    {
      builder.Append("&vote_average.gte=").Append(query.MinRating.Value.ToString(CultureInfo.InvariantCulture));
    }

    if (query.MaxRating.HasValue)
    {
      builder.Append("&vote_average.lte=").Append(query.MaxRating.Value.ToString(CultureInfo.InvariantCulture));
    }

    var page = query.Page is > 0 ? query.Page.Value : 1;
    builder.Append("&page=").Append(page.ToString(CultureInfo.InvariantCulture));

    if (!string.IsNullOrWhiteSpace(query.WatchProviders) && !string.IsNullOrWhiteSpace(query.WatchRegion))
    {
      builder.Append("&with_watch_providers=").Append(Escape(query.WatchProviders))
        .Append("&watch_region=").Append(Escape(query.WatchRegion));
    }

    if (!string.IsNullOrWhiteSpace(query.OriginalLanguage))
    {
      builder.Append("&with_original_language=").Append(Escape(query.OriginalLanguage));
    }

    if (!string.IsNullOrWhiteSpace(query.OriginCountry))
    {
      builder.Append("&with_origin_country=").Append(Escape(query.OriginCountry));
    }

    if (query.WithPeople is > 0)
    {
      builder.Append("&with_people=").Append(query.WithPeople.Value.ToString(CultureInfo.InvariantCulture));
    }

    // Child-mode age filter (movies only — TMDB has no reliable TV certification discover filter).
    if (isMovie && !string.IsNullOrWhiteSpace(query.CertificationCountry) && !string.IsNullOrWhiteSpace(query.CertificationLte))
    {
      builder.Append("&certification_country=").Append(Escape(query.CertificationCountry))
        .Append("&certification.lte=").Append(Escape(query.CertificationLte));
    }

    var json = await GetAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseResults(json, mediaType);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<Genre>> GetGenresAsync(string mediaType, string language, CancellationToken cancellationToken)
  {
    EnsureMediaType(mediaType);

    var json = await GetAsync($"/genre/{mediaType}/list?language={Escape(language)}", cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseGenres(json);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<WatchProvider>> GetWatchProvidersAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
  {
    EnsureMediaType(mediaType);

    var json = await GetAsync(
      $"/watch/providers/{mediaType}?watch_region={Escape(region)}&language={Escape(language)}",
      cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseWatchProviders(json);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<Season>> GetSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken)
  {
    var id = tmdbId.ToString(CultureInfo.InvariantCulture);
    var json = await GetAsync($"/tv/{id}?language={Escape(language)}", cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseSeasons(json);
  }

  /// <inheritdoc />
  public async Task<int?> GetTvdbIdAsync(int tmdbId, CancellationToken cancellationToken)
  {
    var id = tmdbId.ToString(CultureInfo.InvariantCulture);
    var json = await GetAsync($"/tv/{id}/external_ids", cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseTvdbId(json);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<CatalogItem>> GetUpcomingAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
  {
    EnsureMediaType(mediaType);

    var path = string.Equals(mediaType, "movie", StringComparison.Ordinal)
      ? $"/movie/upcoming?language={Escape(language)}&region={Escape(region)}&page=1"
      : $"/tv/on_the_air?language={Escape(language)}&page=1";
    var json = await GetAsync(path, cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseResults(json, mediaType);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<CatalogItem>> GetReleasesAsync(string mediaType, string fromDate, string toDate, string region, string language, string? originalLanguage, string? originCountry, int page, CancellationToken cancellationToken)
  {
    EnsureMediaType(mediaType);

    var isMovie = string.Equals(mediaType, "movie", StringComparison.Ordinal);
    var dateField = isMovie ? "primary_release_date" : "first_air_date";
    var resultPage = page > 0 ? page : 1;

    // Sort by popularity (not date) so the capped result set spans the whole range with the most
    // notable releases, instead of clustering on the earliest days; the caller re-groups by date.
    var builder = new StringBuilder();
    builder.Append("/discover/").Append(mediaType)
      .Append("?language=").Append(Escape(language))
      .Append("&include_adult=false&sort_by=popularity.desc")
      .Append('&').Append(dateField).Append(".gte=").Append(Escape(fromDate))
      .Append('&').Append(dateField).Append(".lte=").Append(Escape(toDate))
      .Append("&page=").Append(resultPage.ToString(CultureInfo.InvariantCulture));

    if (isMovie && !string.IsNullOrWhiteSpace(region))
    {
      builder.Append("&region=").Append(Escape(region));
    }

    if (!string.IsNullOrWhiteSpace(originalLanguage))
    {
      builder.Append("&with_original_language=").Append(Escape(originalLanguage));
    }

    if (!string.IsNullOrWhiteSpace(originCountry))
    {
      builder.Append("&with_origin_country=").Append(Escape(originCountry));
    }

    var json = await GetAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseResults(json, mediaType);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<Episode>> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, string language, CancellationToken cancellationToken)
  {
    var id = tmdbId.ToString(CultureInfo.InvariantCulture);
    var season = seasonNumber.ToString(CultureInfo.InvariantCulture);
    var json = await GetAsync($"/tv/{id}/season/{season}?language={Escape(language)}", cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseEpisodes(json);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<CatalogItem>> GetRecommendationsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
  {
    EnsureMediaType(mediaType);

    var id = tmdbId.ToString(CultureInfo.InvariantCulture);
    var json = await GetAsync($"/{mediaType}/{id}/recommendations?language={Escape(language)}&page=1", cancellationToken).ConfigureAwait(false);
    return TmdbResponseParser.ParseResults(json, mediaType);
  }

  private static void EnsureMediaType(string mediaType)
  {
    if (!string.Equals(mediaType, "movie", StringComparison.Ordinal)
        && !string.Equals(mediaType, "tv", StringComparison.Ordinal))
    {
      throw new ArgumentException("Media type must be 'movie' or 'tv'.", nameof(mediaType));
    }
  }

  private static string SortBy(string? sort, bool isMovie)
  {
    if (string.Equals(sort, "rating", StringComparison.Ordinal))
    {
      return "vote_average.desc";
    }

    if (string.Equals(sort, "release", StringComparison.Ordinal))
    {
      return isMovie ? "primary_release_date.desc" : "first_air_date.desc";
    }

    return "popularity.desc";
  }

  private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

  private async Task<string> GetAsync(string relativePathWithQuery, CancellationToken cancellationToken)
  {
    var apiKey = _config().TmdbApiKey ?? string.Empty;
    if (string.IsNullOrWhiteSpace(apiKey))
    {
      throw new InvalidOperationException("TMDB API key is not configured.");
    }

    _logger.LogDebug("Requesting TMDB {Path}", relativePathWithQuery);

    var separator = relativePathWithQuery.Contains('?', StringComparison.Ordinal) ? '&' : '?';
    var uri = new Uri($"{_baseUrl}{relativePathWithQuery}{separator}api_key={Escape(apiKey)}");

    var client = _httpClientFactory.CreateClient(NamedClient.Default);
    using var response = await UpstreamTimeout.GetAsync(client, uri, UpstreamTimeout.Tmdb, cancellationToken).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
  }
}
