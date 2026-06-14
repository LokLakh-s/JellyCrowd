using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// ntfy notification channel (publishes to a topic; defaults to the public ntfy.sh server).
/// </summary>
public sealed class NtfyNotifier : ITextNotifier
{
  private const string DefaultServer = "https://ntfy.sh";

  private readonly IHttpClientFactory _httpClientFactory;

  /// <summary>
  /// Initializes a new instance of the <see cref="NtfyNotifier"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory.</param>
  public NtfyNotifier(IHttpClientFactory httpClientFactory)
  {
    _httpClientFactory = httpClientFactory;
  }

  /// <inheritdoc />
  public string Channel => "ntfy";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return !string.IsNullOrWhiteSpace(config.NtfyTopic);
  }

  /// <inheritdoc />
  public Task SendAsync(PluginConfiguration config, string title, string body, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(config);
    if (!IsConfigured(config))
    {
      throw new InvalidOperationException("ntfy is not configured (a topic is required).");
    }

    var server = string.IsNullOrWhiteSpace(config.NtfyServer) ? DefaultServer : config.NtfyServer.TrimEnd('/');
    var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server + "/" + config.NtfyTopic))
    {
      Content = new StringContent(body, Encoding.UTF8, "text/plain")
    };
    request.Headers.TryAddWithoutValidation("Title", title);
    if (!string.IsNullOrWhiteSpace(config.NtfyToken))
    {
      request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + config.NtfyToken);
    }

    return NotifierHttp.SendAsync(_httpClientFactory, request, cancellationToken);
  }
}
