using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Download backend that POSTs the request payload (JSON) to a configured URL with optional headers.
/// Lets the admin wire Jelly Crowd to any automation (a Radarr/Sonarr relay, n8n, a script behind HTTP…).
/// </summary>
public sealed class WebhookDownloadClient : IDownloadClient
{
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="WebhookDownloadClient"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  public WebhookDownloadClient(IHttpClientFactory httpClientFactory, Func<PluginConfiguration> config)
  {
    _httpClientFactory = httpClientFactory;
    _config = config;
  }

  /// <inheritdoc />
  public string Backend => "webhook";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return !string.IsNullOrWhiteSpace(config.DownloadWebhookUrl);
  }

  /// <inheritdoc />
  public Task DispatchAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    return PostAsync(dispatch, cancellationToken);
  }

  /// <inheritdoc />
  public Task TestAsync(CancellationToken cancellationToken)
  {
    var sample = new DownloadDispatch
    {
      RequestId = Guid.Empty,
      UserName = "Jelly Crowd test",
      TmdbId = 603,
      MediaType = "movie",
      Title = "The Matrix",
      Year = 1999,
      ReleaseDate = "1999-03-30",
      RequestedAt = DateTime.UtcNow,
      TmdbUrl = "https://www.themoviedb.org/movie/603"
    };
    return PostAsync(sample, cancellationToken);
  }

  /// <inheritdoc />
  public Task CancelAsync(DownloadDispatch dispatch, CancellationToken cancellationToken) => Task.CompletedTask;

  /// <inheritdoc />
  public Task<bool> PurgeAsync(DownloadDispatch dispatch, CancellationToken cancellationToken) => Task.FromResult(true);

  /// <inheritdoc />
  public Task RetryAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    return PostAsync(dispatch, cancellationToken);
  }

  private async Task PostAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    var config = _config();
    if (!IsConfigured(config))
    {
      throw new InvalidOperationException("The download webhook URL is not configured.");
    }

    var client = _httpClientFactory.CreateClient(NamedClient.Default);
    var json = JsonSerializer.Serialize(dispatch);
    using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(config.DownloadWebhookUrl))
    {
      Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    foreach (var header in WebhookHeaders.Parse(config.DownloadWebhookHeaders))
    {
      request.Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
  }
}
