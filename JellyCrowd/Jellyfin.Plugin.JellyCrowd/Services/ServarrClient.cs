using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IServarrClient"/> backed by the Radarr/Sonarr v3 REST API (auth via the
/// <c>X-Api-Key</c> header).
/// </summary>
public sealed class ServarrClient : IServarrClient
{
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ILogger<ServarrClient> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="ServarrClient"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory.</param>
  /// <param name="logger">The logger.</param>
  public ServarrClient(IHttpClientFactory httpClientFactory, ILogger<ServarrClient> logger)
  {
    _httpClientFactory = httpClientFactory;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task TestAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
  {
    await GetStringAsync(baseUrl, apiKey, "/system/status", cancellationToken).ConfigureAwait(false);
  }

  /// <inheritdoc />
  public async Task<ServarrResources> GetResourcesAsync(string baseUrl, string apiKey, bool includeLanguageProfiles, CancellationToken cancellationToken)
  {
    var resources = new ServarrResources();

    var rootJson = await GetStringAsync(baseUrl, apiKey, "/rootfolder", cancellationToken).ConfigureAwait(false);
    foreach (var resource in ServarrResponseParser.ParseResources(rootJson, "path"))
    {
      resources.RootFolders.Add(resource);
    }

    var qualityJson = await GetStringAsync(baseUrl, apiKey, "/qualityprofile", cancellationToken).ConfigureAwait(false);
    foreach (var resource in ServarrResponseParser.ParseResources(qualityJson, "name"))
    {
      resources.QualityProfiles.Add(resource);
    }

    if (includeLanguageProfiles)
    {
      try
      {
        var languageJson = await GetStringAsync(baseUrl, apiKey, "/languageprofile", cancellationToken).ConfigureAwait(false);
        foreach (var resource in ServarrResponseParser.ParseResources(languageJson, "name"))
        {
          resources.LanguageProfiles.Add(resource);
        }
      }
#pragma warning disable CA1031 // Language profiles only exist on Sonarr v3; absence (404) is fine.
      catch (Exception ex)
#pragma warning restore CA1031
      {
        _logger.LogDebug(ex, "No language profiles (Sonarr v4 or unavailable).");
      }
    }

    return resources;
  }

  /// <inheritdoc />
  public async Task<JsonObject?> LookupMovieAsync(string baseUrl, string apiKey, int tmdbId, CancellationToken cancellationToken)
  {
    var json = await GetStringAsync(
      baseUrl,
      apiKey,
      "/movie/lookup/tmdb?tmdbId=" + tmdbId.ToString(CultureInfo.InvariantCulture),
      cancellationToken).ConfigureAwait(false);
    return ParseObject(json);
  }

  /// <inheritdoc />
  public async Task<JsonObject?> LookupSeriesAsync(string baseUrl, string apiKey, int tvdbId, CancellationToken cancellationToken)
  {
    var term = Uri.EscapeDataString("tvdb:" + tvdbId.ToString(CultureInfo.InvariantCulture));
    var json = await GetStringAsync(baseUrl, apiKey, "/series/lookup?term=" + term, cancellationToken).ConfigureAwait(false);
    return ParseObject(json);
  }

  /// <inheritdoc />
  public Task AddMovieAsync(string baseUrl, string apiKey, JsonObject body, CancellationToken cancellationToken)
    => PostAsync(baseUrl, apiKey, "/movie", body, cancellationToken);

  /// <inheritdoc />
  public Task AddSeriesAsync(string baseUrl, string apiKey, JsonObject body, CancellationToken cancellationToken)
    => PostAsync(baseUrl, apiKey, "/series", body, cancellationToken);

  /// <inheritdoc />
  public Task<string> GetQueueAsync(string baseUrl, string apiKey, bool forSonarr, CancellationToken cancellationToken)
  {
    var path = forSonarr
      ? "/queue?page=1&pageSize=200&includeSeries=true&includeEpisode=true"
      : "/queue?page=1&pageSize=200&includeMovie=true";
    return GetStringAsync(baseUrl, apiKey, path, cancellationToken);
  }

  private static JsonObject? ParseObject(string json)
  {
    var node = JsonNode.Parse(json);
    return node switch
    {
      JsonObject obj => obj,
      JsonArray { Count: > 0 } array when array[0] is JsonObject first => first,
      _ => null
    };
  }

  private HttpRequestMessage CreateRequest(HttpMethod method, string baseUrl, string apiKey, string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
    var uri = new Uri(baseUrl.TrimEnd('/') + "/api/v3" + path);
    var request = new HttpRequestMessage(method, uri);
    request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
    return request;
  }

  private async Task<string> GetStringAsync(string baseUrl, string apiKey, string path, CancellationToken cancellationToken)
  {
    using var request = CreateRequest(HttpMethod.Get, baseUrl, apiKey, path);
    var client = _httpClientFactory.CreateClient(NamedClient.Default);
    using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
  }

  private async Task PostAsync(string baseUrl, string apiKey, string path, JsonObject body, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(body);
    using var request = CreateRequest(HttpMethod.Post, baseUrl, apiKey, path);
    request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    var client = _httpClientFactory.CreateClient(NamedClient.Default);
    using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
  }
}
