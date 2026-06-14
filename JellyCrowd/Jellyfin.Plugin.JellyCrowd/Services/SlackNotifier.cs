using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Slack notification channel (incoming webhook).
/// </summary>
public sealed class SlackNotifier : ITextNotifier
{
  private readonly IHttpClientFactory _httpClientFactory;

  /// <summary>
  /// Initializes a new instance of the <see cref="SlackNotifier"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory.</param>
  public SlackNotifier(IHttpClientFactory httpClientFactory)
  {
    _httpClientFactory = httpClientFactory;
  }

  /// <inheritdoc />
  public string Channel => "slack";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return !string.IsNullOrWhiteSpace(config.SlackWebhookUrl);
  }

  /// <inheritdoc />
  public Task SendAsync(PluginConfiguration config, string title, string body, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(config);
    if (!IsConfigured(config))
    {
      throw new InvalidOperationException("The Slack webhook URL is not configured.");
    }

    return NotifierHttp.PostJsonAsync(_httpClientFactory, config.SlackWebhookUrl, new { text = title + "\n" + body }, cancellationToken);
  }
}
