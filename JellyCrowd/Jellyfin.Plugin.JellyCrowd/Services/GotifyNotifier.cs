using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Gotify notification channel (server message API).
/// </summary>
public sealed class GotifyNotifier : ITextNotifier
{
  private readonly IHttpClientFactory _httpClientFactory;

  /// <summary>
  /// Initializes a new instance of the <see cref="GotifyNotifier"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory.</param>
  public GotifyNotifier(IHttpClientFactory httpClientFactory)
  {
    _httpClientFactory = httpClientFactory;
  }

  /// <inheritdoc />
  public string Channel => "gotify";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return !string.IsNullOrWhiteSpace(config.GotifyServer) && !string.IsNullOrWhiteSpace(config.GotifyToken);
  }

  /// <inheritdoc />
  public Task SendAsync(PluginConfiguration config, string title, string body, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(config);
    if (!IsConfigured(config))
    {
      throw new InvalidOperationException("Gotify is not configured (server URL and token are required).");
    }

    var url = config.GotifyServer.TrimEnd('/') + "/message?token=" + Uri.EscapeDataString(config.GotifyToken);
    return NotifierHttp.PostJsonAsync(_httpClientFactory, url, new { title, message = body }, cancellationToken);
  }
}
