using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Integration;

/// <summary>
/// End-to-end HTTP integration tests for the Radarr/Sonarr client and dispatch flow, exercised against a
/// real in-process HTTP server (WireMock.Net) rather than a mocked message handler — validating URL and
/// query construction, the <c>X-Api-Key</c> header, request bodies and status handling over real sockets.
/// </summary>
public sealed class ServarrHttpIntegrationTests : IDisposable
{
  private readonly WireMockServer _server = WireMockServer.Start();

  public void Dispose() => _server.Stop();

  private ServarrClient Client() => new(new RealHttpClientFactory(), NullLogger<ServarrClient>.Instance);

  private PluginConfiguration RadarrConfig() => new()
  {
    RadarrUrl = _server.Url!,
    RadarrApiKey = "KEY",
    RadarrRootFolderPath = "/movies",
    RadarrQualityProfileId = 4
  };

  [Fact]
  public async Task GetMovieByTmdb_RoundTrips_ParsesResult_AndSendsApiKeyHeader()
  {
    _server
      .Given(Request.Create().WithPath("/api/v3/movie").UsingGet().WithParam("tmdbId", "603"))
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("[{\"id\":42,\"title\":\"The Matrix\"}]"));

    var movie = await Client().GetMovieByTmdbAsync(_server.Url!, "KEY", 603, CancellationToken.None);

    Assert.Equal(42, movie!["id"]!.GetValue<int>());
    var entry = Assert.Single(_server.LogEntries);
    var apiKey = entry.RequestMessage!.Headers!
      .First(h => string.Equals(h.Key, "X-Api-Key", StringComparison.OrdinalIgnoreCase)).Value.Single();
    Assert.Equal("KEY", apiKey);
  }

  [Fact]
  public async Task Command_PostsBody_OverHttp()
  {
    _server
      .Given(Request.Create().WithPath("/api/v3/command").UsingPost())
      .RespondWith(Response.Create().WithStatusCode(201).WithBody("{}"));

    await Client().CommandAsync(
      _server.Url!,
      "KEY",
      new JsonObject { ["name"] = "RescanMovie", ["movieId"] = 42 },
      CancellationToken.None);

    var entry = Assert.Single(_server.LogEntries);
    Assert.Equal("POST", entry.RequestMessage!.Method);
    Assert.Contains("RescanMovie", entry.RequestMessage!.Body!, StringComparison.Ordinal);
    Assert.Contains("42", entry.RequestMessage!.Body!, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Test_Succeeds_On200_AndThrows_On401()
  {
    _server
      .Given(Request.Create().WithPath("/api/v3/system/status").UsingGet())
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"version\":\"4.0\"}"));

    await Client().TestAsync(_server.Url!, "KEY", CancellationToken.None); // 200 → no throw

    _server.Reset();
    _server
      .Given(Request.Create().WithPath("/api/v3/system/status").UsingGet())
      .RespondWith(Response.Create().WithStatusCode(401));

    await Assert.ThrowsAsync<HttpRequestException>(
      () => Client().TestAsync(_server.Url!, "KEY", CancellationToken.None));
  }

  [Fact]
  public async Task GetQueue_SelectsIncludeMovie_ForRadarr_AndIncludeSeries_ForSonarr()
  {
    _server
      .Given(Request.Create().WithPath("/api/v3/queue").UsingGet().WithParam("includeMovie", "true"))
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"kind\":\"radarr\"}"));
    _server
      .Given(Request.Create().WithPath("/api/v3/queue").UsingGet().WithParam("includeSeries", "true"))
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"kind\":\"sonarr\"}"));

    var radarr = await Client().GetQueueAsync(_server.Url!, "KEY", forSonarr: false, CancellationToken.None);
    var sonarr = await Client().GetQueueAsync(_server.Url!, "KEY", forSonarr: true, CancellationToken.None);

    Assert.Contains("radarr", radarr, StringComparison.Ordinal);
    Assert.Contains("sonarr", sonarr, StringComparison.Ordinal);
  }

  [Fact]
  public async Task GetProwlarrIndexers_UsesTheV1Api()
  {
    // A response is only returned when the request lands on /api/v1/indexer; a v3 path would 404 and throw.
    _server
      .Given(Request.Create().WithPath("/api/v1/indexer").UsingGet())
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("[{\"id\":1,\"enable\":true}]"));

    var json = await Client().GetProwlarrIndexersAsync(_server.Url!, "KEY", CancellationToken.None);

    Assert.Contains("\"id\":1", json, StringComparison.Ordinal);
  }

  [Fact]
  public async Task DispatchFlow_Rescan_Movie_LooksUpThenSendsRescanCommand()
  {
    // Full Part B path over real HTTP: the media exists in Radarr → send a RescanMovie command so a
    // manually-placed file is imported from disk.
    _server
      .Given(Request.Create().WithPath("/api/v3/movie").UsingGet().WithParam("tmdbId", "603"))
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("[{\"id\":42}]"));
    _server
      .Given(Request.Create().WithPath("/api/v3/command").UsingPost())
      .RespondWith(Response.Create().WithStatusCode(201).WithBody("{}"));

    var config = RadarrConfig();
    var dispatchClient = new ServarrDownloadClient(Client(), new StubTmdbClient(), () => config);

    await dispatchClient.RescanAsync(
      new DownloadDispatch { TmdbId = 603, MediaType = "movie", Title = "The Matrix" },
      CancellationToken.None);

    var command = Assert.Single(_server.LogEntries, e => e.RequestMessage!.Path == "/api/v3/command");
    Assert.Contains("RescanMovie", command.RequestMessage!.Body!, StringComparison.Ordinal);
    Assert.Contains("42", command.RequestMessage!.Body!, StringComparison.Ordinal);
  }

  [Fact]
  public async Task DispatchFlow_Movie_AddsToRadarr_WhenNotYetPresent()
  {
    // request → dispatch flow: not in Radarr yet → look up on TMDB via Radarr → add with the configured
    // root folder + quality profile (and a search).
    _server
      .Given(Request.Create().WithPath("/api/v3/movie").UsingGet().WithParam("tmdbId", "603"))
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("[]")); // absent
    _server
      .Given(Request.Create().WithPath("/api/v3/movie/lookup/tmdb").UsingGet())
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"title\":\"The Matrix\",\"tmdbId\":603,\"year\":1999}"));
    _server
      .Given(Request.Create().WithPath("/api/v3/movie").UsingPost())
      .RespondWith(Response.Create().WithStatusCode(201).WithBody("{\"id\":42}"));

    var config = RadarrConfig();
    var dispatchClient = new ServarrDownloadClient(Client(), new StubTmdbClient(), () => config);

    await dispatchClient.DispatchAsync(
      new DownloadDispatch { TmdbId = 603, MediaType = "movie", Title = "The Matrix", Year = 1999 },
      CancellationToken.None);

    var add = Assert.Single(
      _server.LogEntries,
      e => e.RequestMessage!.Method == "POST" && e.RequestMessage!.Path == "/api/v3/movie");
    Assert.Contains("qualityProfileId", add.RequestMessage!.Body!, StringComparison.Ordinal);
    Assert.Contains("/movies", add.RequestMessage!.Body!, StringComparison.Ordinal);
    Assert.Contains("The Matrix", add.RequestMessage!.Body!, StringComparison.Ordinal);
  }

  // ---------- TMDB ----------

  private TmdbClient Tmdb(string apiKey) => new(
    new RealHttpClientFactory(),
    () => new PluginConfiguration { TmdbApiKey = apiKey },
    NullLogger<TmdbClient>.Instance,
    _server.Url!);

  [Fact]
  public async Task Tmdb_GetGenres_RoundTrips_AndSendsApiKeyInQuery()
  {
    _server
      .Given(Request.Create().WithPath("/genre/movie/list").UsingGet().WithParam("api_key", "TMKEY"))
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"genres\":[{\"id\":28,\"name\":\"Action\"}]}"));

    var genres = await Tmdb("TMKEY").GetGenresAsync("movie", "en-US", CancellationToken.None);

    Assert.Equal("Action", Assert.Single(genres).Name);
  }

  [Fact]
  public async Task Tmdb_Throws_WhenApiKeyMissing()
  {
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => Tmdb(string.Empty).GetGenresAsync("movie", "en-US", CancellationToken.None));

    Assert.Empty(_server.LogEntries); // guarded before any HTTP call is made
  }

  [Fact]
  public async Task Tmdb_Discover_SendsFilterQueryParams()
  {
    // The stub only matches when with_genres AND api_key are present, so a successful (matched) response
    // proves the filter + auth query were built and sent correctly.
    _server
      .Given(Request.Create().WithPath("/discover/movie").UsingGet()
        .WithParam("with_genres", "28").WithParam("api_key", "TMKEY"))
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"results\":[]}"));

    var results = await Tmdb("TMKEY").DiscoverAsync(
      "movie", new DiscoverQuery { Genres = "28" }, "en-US", CancellationToken.None);

    Assert.Empty(results);
    Assert.Single(_server.LogEntries);
  }

  private sealed class RealHttpClientFactory : IHttpClientFactory
  {
    public HttpClient CreateClient(string name) => new();
  }
}
