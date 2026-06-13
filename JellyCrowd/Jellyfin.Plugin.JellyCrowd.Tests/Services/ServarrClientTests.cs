using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrClient"/> using a stub HTTP handler.
/// </summary>
public class ServarrClientTests
{
  [Fact]
  public async Task GetResourcesAsync_ParsesFoldersProfilesAndSendsApiKey()
  {
    var handler = new StubHandler(req =>
    {
      var path = req.RequestUri!.AbsolutePath;
      var body = path switch
      {
        "/api/v3/rootfolder" => "[{\"id\":1,\"path\":\"/movies\"}]",
        "/api/v3/qualityprofile" => "[{\"id\":4,\"name\":\"HD-1080p\"}]",
        "/api/v3/languageprofile" => "[{\"id\":1,\"name\":\"English\"}]",
        _ => "[]"
      };
      return Json(body);
    });
    var client = new ServarrClient(new StubFactory(handler), NullLogger<ServarrClient>.Instance);

    var resources = await client.GetResourcesAsync("http://host:8989/", "key", includeLanguageProfiles: true, CancellationToken.None);

    Assert.Equal("/movies", Assert.Single(resources.RootFolders).Name);
    Assert.Equal("HD-1080p", Assert.Single(resources.QualityProfiles).Name);
    Assert.Equal("English", Assert.Single(resources.LanguageProfiles).Name);
    Assert.All(handler.Requests, r => Assert.Equal("key", r.Headers.GetValues("X-Api-Key").Single()));
  }

  [Fact]
  public async Task LookupSeriesAsync_UsesTvdbTerm_AndReturnsFirst()
  {
    var handler = new StubHandler(_ => Json("[{\"title\":\"Breaking Bad\",\"tvdbId\":81189}]"));
    var client = new ServarrClient(new StubFactory(handler), NullLogger<ServarrClient>.Instance);

    var result = await client.LookupSeriesAsync("http://host:8989", "key", 81189, CancellationToken.None);

    Assert.NotNull(result);
    Assert.Equal(81189, result!["tvdbId"]!.GetValue<int>());
    Assert.Contains("/api/v3/series/lookup", handler.Requests[0].RequestUri!.ToString(), StringComparison.Ordinal);
    Assert.Contains("term=tvdb%3A81189", handler.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
  }

  [Fact]
  public async Task AddMovieAsync_PostsBodyToMovieEndpoint()
  {
    string? sentBody = null;
    var handler = new StubHandler(req =>
    {
      sentBody = req.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();
      return new HttpResponseMessage(HttpStatusCode.Created);
    });
    var client = new ServarrClient(new StubFactory(handler), NullLogger<ServarrClient>.Instance);

    await client.AddMovieAsync("http://host:7878", "key", new JsonObject { ["title"] = "The Matrix" }, CancellationToken.None);

    Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
    Assert.Equal("/api/v3/movie", handler.Requests[0].RequestUri!.AbsolutePath);
    Assert.Contains("The Matrix", sentBody, StringComparison.Ordinal);
  }

  private static HttpResponseMessage Json(string body)
    => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

  private sealed class StubHandler : HttpMessageHandler
  {
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Requests.Add(request);
      return Task.FromResult(_responder(request));
    }
  }

  private sealed class StubFactory : IHttpClientFactory
  {
    private readonly HttpMessageHandler _handler;

    public StubFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
  }
}
