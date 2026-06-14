using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Generic notification channel: POSTs a <c>{title, body}</c> JSON payload to a configured URL.
/// </summary>
public sealed class WebhookNotifier : ITextNotifier
{
  private readonly IHttpClientFactory _httpClientFactory;

  /// <summary>
  /// Initializes a new instance of the <see cref="WebhookNotifier"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory.</param>
  public WebhookNotifier(IHttpClientFactory httpClientFactory)
  {
    _httpClientFactory = httpClientFactory;
  }

  /// <inheritdoc />
  public string Channel => "webhook";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return !string.IsNullOrWhiteSpace(config.NotifyWebhookUrl);
  }

  /// <inheritdoc />
  public Task SendAsync(PluginConfiguration config, string title, string body, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(config);
    if (!IsConfigured(config))
    {
      throw new InvalidOperationException("The notification webhook URL is not configured.");
    }

    return NotifierHttp.PostJsonAsync(_httpClientFactory, config.NotifyWebhookUrl, new { title, body }, cancellationToken);
  }
}
