using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Bounds how long a call to an upstream (TMDB, Sonarr/Radarr) may take.
/// <para>
/// The shared <c>HttpClient</c> comes from Jellyfin's factory with the framework default: <b>100 seconds</b>.
/// A refused connection fails instantly, but an upstream that ACCEPTS the connection and never answers —
/// a firewall that drops, a service that is up but wedged — parks the caller for the full 100s. Measured
/// against a real server, the catalog then made a user wait 100 seconds and answered 500. Worse, each of
/// those requests holds a Jellyfin request thread the whole time, so a handful of users refreshing during
/// a TMDB outage can starve the pool and take the SERVER down, not just the plugin.
/// </para>
/// <para>
/// A timeout is reported as an <see cref="HttpRequestException"/> on purpose: every caller already treats
/// a transport failure as "upstream unavailable" and answers 503, whereas a bare cancellation would
/// bubble out as a 500.
/// </para>
/// </summary>
internal static class UpstreamTimeout
{
  /// <summary>How long TMDB may take. It is a read-only lookup on the hot path of every catalog page.</summary>
  public static readonly TimeSpan Tmdb = TimeSpan.FromSeconds(15);

  /// <summary>How long Sonarr/Radarr may take. A little longer: adding a series does real work.</summary>
  public static readonly TimeSpan Servarr = TimeSpan.FromSeconds(20);

  /// <summary>How long an admin-configured outbound endpoint (a notifier, the download webhook) may take.</summary>
  public static readonly TimeSpan Outbound = TimeSpan.FromSeconds(15);

  /// <summary>
  /// Sends a request, failing it as a transport error if the upstream has not answered within
  /// <paramref name="timeout"/>.
  /// </summary>
  /// <param name="client">The HTTP client.</param>
  /// <param name="request">The request to send (owned by the caller).</param>
  /// <param name="timeout">How long the upstream may take.</param>
  /// <param name="cancellationToken">The caller's cancellation token.</param>
  /// <returns>The response.</returns>
  public static async Task<HttpResponseMessage> SendAsync(
    HttpClient client,
    HttpRequestMessage request,
    TimeSpan timeout,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(client);

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(timeout);

    try
    {
      return await client.SendAsync(request, cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      // Ours, not the caller's: the upstream ran out of time.
      throw new HttpRequestException(string.Format(
        CultureInfo.InvariantCulture,
        "The upstream did not respond within {0:0}s.",
        timeout.TotalSeconds));
    }
  }

  /// <summary>
  /// GETs a URI under the same time bound.
  /// </summary>
  /// <param name="client">The HTTP client.</param>
  /// <param name="uri">The URI to GET.</param>
  /// <param name="timeout">How long the upstream may take.</param>
  /// <param name="cancellationToken">The caller's cancellation token.</param>
  /// <returns>The response.</returns>
  public static async Task<HttpResponseMessage> GetAsync(
    HttpClient client,
    Uri uri,
    TimeSpan timeout,
    CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
    return await SendAsync(client, request, timeout, cancellationToken).ConfigureAwait(false);
  }
}
