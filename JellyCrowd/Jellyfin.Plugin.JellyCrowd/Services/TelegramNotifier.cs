using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Telegram notification channel (bot sendMessage).
/// </summary>
public sealed class TelegramNotifier : ITextNotifier
{
  private readonly IHttpClientFactory _httpClientFactory;

  /// <summary>
  /// Initializes a new instance of the <see cref="TelegramNotifier"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory.</param>
  public TelegramNotifier(IHttpClientFactory httpClientFactory)
  {
    _httpClientFactory = httpClientFactory;
  }

  /// <inheritdoc />
  public string Channel => "telegram";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return !string.IsNullOrWhiteSpace(config.TelegramBotToken) && !string.IsNullOrWhiteSpace(config.TelegramChatId);
  }

  /// <inheritdoc />
  public Task SendAsync(PluginConfiguration config, string title, string body, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(config);
    if (!IsConfigured(config))
    {
      throw new InvalidOperationException("Telegram is not configured (bot token and chat id are required).");
    }

    var url = "https://api.telegram.org/bot" + config.TelegramBotToken + "/sendMessage";
    return NotifierHttp.PostJsonAsync(_httpClientFactory, url, new { chat_id = config.TelegramChatId, text = title + "\n" + body }, cancellationToken);
  }
}
