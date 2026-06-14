using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pushover notification channel (messages API, form-encoded).
/// </summary>
public sealed class PushoverNotifier : ITextNotifier
{
  private readonly IHttpClientFactory _httpClientFactory;

  /// <summary>
  /// Initializes a new instance of the <see cref="PushoverNotifier"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory.</param>
  public PushoverNotifier(IHttpClientFactory httpClientFactory)
  {
    _httpClientFactory = httpClientFactory;
  }

  /// <inheritdoc />
  public string Channel => "pushover";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return !string.IsNullOrWhiteSpace(config.PushoverToken) && !string.IsNullOrWhiteSpace(config.PushoverUser);
  }

  /// <inheritdoc />
  public Task SendAsync(PluginConfiguration config, string title, string body, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(config);
    if (!IsConfigured(config))
    {
      throw new InvalidOperationException("Pushover is not configured (API token and user key are required).");
    }

    var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.pushover.net/1/messages.json"))
    {
      Content = new FormUrlEncodedContent(new Dictionary<string, string>
      {
        ["token"] = config.PushoverToken,
        ["user"] = config.PushoverUser,
        ["title"] = title,
        ["message"] = body
      })
    };
    return NotifierHttp.SendAsync(_httpClientFactory, request, cancellationToken);
  }
}
