using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Small HTTP helpers shared by the text notification channels.
/// </summary>
internal static class NotifierHttp
{
  /// <summary>
  /// POSTs a JSON payload and throws on a non-success status.
  /// </summary>
  /// <param name="factory">The HTTP client factory.</param>
  /// <param name="url">The target URL.</param>
  /// <param name="payload">The payload to serialize as JSON.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes on success.</returns>
  public static async Task PostJsonAsync(IHttpClientFactory factory, string url, object payload, CancellationToken cancellationToken)
  {
    var client = factory.CreateClient(NamedClient.Default);
    using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    using var response = await client.PostAsync(new Uri(url), content, cancellationToken).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
  }

  /// <summary>
  /// Sends a prepared request and throws on a non-success status.
  /// </summary>
  /// <param name="factory">The HTTP client factory.</param>
  /// <param name="request">The request to send.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes on success.</returns>
  public static async Task SendAsync(IHttpClientFactory factory, HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var client = factory.CreateClient(NamedClient.Default);
    using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
  }
}
