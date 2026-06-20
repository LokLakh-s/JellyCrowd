using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="DownloadController"/>.
/// </summary>
public class DownloadControllerTests
{
  [Fact]
  public async Task Test_Success_ReturnsNoContent()
  {
    var controller = new DownloadController(new FakeDispatcher(null), new FakeServarrClient());

    var result = await controller.Test(CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
  }

  [Fact]
  public async Task Test_Failure_ReturnsBadRequest()
  {
    var controller = new DownloadController(new FakeDispatcher(new InvalidOperationException("no backend")), new FakeServarrClient());

    var result = await controller.Test(CancellationToken.None);

    var obj = Assert.IsType<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
  }

  [Fact]
  public async Task ServarrResources_MissingUrlOrKey_ReturnsBadRequest()
  {
    var controller = new DownloadController(new FakeDispatcher(null), new FakeServarrClient());

    var result = await controller.ServarrResources(new ServarrResourcesRequest { Service = "radarr" }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task ServarrResources_Valid_ReturnsResources()
  {
    var controller = new DownloadController(new FakeDispatcher(null), new FakeServarrClient());

    var result = await controller.ServarrResources(
      new ServarrResourcesRequest { Service = "radarr", Url = "http://localhost:7878", ApiKey = "k" },
      CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
  }

  private sealed class FakeDispatcher : IDownloadDispatcher
  {
    private readonly Exception? _error;

    public FakeDispatcher(Exception? error) => _error = error;

    public Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task DispatchDueAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TestActiveAsync(CancellationToken cancellationToken)
      => _error is null ? Task.CompletedTask : throw _error;

    public Task CancelAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> RetryAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(true);
  }

  private sealed class FakeServarrClient : IServarrClient
  {
    public Task TestAsync(string baseUrl, string apiKey, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ServarrResources> GetResourcesAsync(string baseUrl, string apiKey, bool includeLanguageProfiles, CancellationToken cancellationToken)
      => Task.FromResult(new ServarrResources());

    public Task<JsonObject?> LookupMovieAsync(string baseUrl, string apiKey, int tmdbId, CancellationToken cancellationToken)
      => Task.FromResult<JsonObject?>(null);

    public Task<JsonObject?> LookupSeriesAsync(string baseUrl, string apiKey, int tvdbId, CancellationToken cancellationToken)
      => Task.FromResult<JsonObject?>(null);

    public Task AddMovieAsync(string baseUrl, string apiKey, JsonObject body, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AddSeriesAsync(string baseUrl, string apiKey, JsonObject body, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> GetQueueAsync(string baseUrl, string apiKey, bool forSonarr, CancellationToken cancellationToken) => Task.FromResult("[]");

    public Task<JsonObject?> GetMovieByTmdbAsync(string baseUrl, string apiKey, int tmdbId, CancellationToken cancellationToken) => Task.FromResult<JsonObject?>(null);

    public Task<JsonObject?> GetSeriesByTvdbAsync(string baseUrl, string apiKey, int tvdbId, CancellationToken cancellationToken) => Task.FromResult<JsonObject?>(null);

    public Task<string> GetEpisodesAsync(string baseUrl, string apiKey, int seriesId, CancellationToken cancellationToken) => Task.FromResult("[]");

    public Task CommandAsync(string baseUrl, string apiKey, JsonObject body, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteMovieAsync(string baseUrl, string apiKey, int movieId, bool deleteFiles, CancellationToken cancellationToken) => Task.CompletedTask;
  }
}
