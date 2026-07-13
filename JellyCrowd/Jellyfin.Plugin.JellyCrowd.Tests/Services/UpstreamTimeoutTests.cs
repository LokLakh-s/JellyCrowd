using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="UpstreamTimeout"/> — the bound that stops a wedged upstream from parking a
/// Jellyfin request thread for HttpClient's 100-second default.
/// </summary>
public class UpstreamTimeoutTests
{
  // Accepts the request and never answers — a firewall that drops, or a service that is up but wedged.
  private sealed class HangingHandler : HttpMessageHandler
  {
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
      throw new InvalidOperationException("unreachable");
    }
  }

  private sealed class OkHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
  }

  [Fact]
  public async Task HungUpstream_FailsAsATransportError_WithinTheTimeout()
  {
    using var client = new HttpClient(new HangingHandler());
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://upstream.test/x");

    var started = DateTime.UtcNow;

    // An HttpRequestException, NOT a cancellation: every caller already maps a transport failure to a 503
    // ("upstream unavailable"), whereas a bare cancellation bubbles out of the controller as a 500.
    await Assert.ThrowsAsync<HttpRequestException>(() =>
      UpstreamTimeout.SendAsync(client, request, TimeSpan.FromMilliseconds(300), CancellationToken.None));

    Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "it must give up on its own, not wait for HttpClient's default");
  }

  [Fact]
  public async Task CallerCancellation_StaysACancellation_AndIsNotDisguisedAsAnUpstreamFailure()
  {
    using var client = new HttpClient(new HangingHandler());
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://upstream.test/x");
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();

    // The client went away: that is not the upstream's fault and must not be reported as one.
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
      UpstreamTimeout.SendAsync(client, request, TimeSpan.FromSeconds(30), cts.Token));
  }

  [Fact]
  public async Task HealthyUpstream_PassesStraightThrough()
  {
    using var client = new HttpClient(new OkHandler());
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://upstream.test/x");

    using var response = await UpstreamTimeout.SendAsync(client, request, TimeSpan.FromSeconds(5), CancellationToken.None);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_IsBoundedToo()
  {
    using var client = new HttpClient(new HangingHandler());

    await Assert.ThrowsAsync<HttpRequestException>(() =>
      UpstreamTimeout.GetAsync(client, new Uri("https://upstream.test/x"), TimeSpan.FromMilliseconds(300), CancellationToken.None));
  }

  [Fact]
  public void TheBoundsAreWellUnderHttpClientsHundredSecondDefault()
  {
    Assert.True(UpstreamTimeout.Tmdb < TimeSpan.FromSeconds(30));
    Assert.True(UpstreamTimeout.Servarr < TimeSpan.FromSeconds(30));
    Assert.True(UpstreamTimeout.Outbound < TimeSpan.FromSeconds(30));
  }
}
