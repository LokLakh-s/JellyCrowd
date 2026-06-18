using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrStatusService"/>.
/// </summary>
public class ServarrStatusServiceTests
{
  private static PluginConfiguration ServarrConfig() => new()
  {
    DownloadBackend = "servarr",
    RadarrUrl = "http://localhost:7878",
    RadarrApiKey = "rk",
    SonarrUrl = "http://localhost:8989",
    SonarrApiKey = "sk"
  };

  private static ServarrStatusService Create(IServarrClient servarr, ITmdbClient tmdb, PluginConfiguration config)
    => new(servarr, tmdb, () => config, NullLogger<ServarrStatusService>.Instance);

  [Fact]
  public async Task GetStatusesAsync_MatchesMovieByTmdb()
  {
    var id = Guid.NewGuid();
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetQueueAsync("http://localhost:7878", "rk", false, It.IsAny<CancellationToken>()))
      .ReturnsAsync("""{ "records": [ { "size": 100, "sizeleft": 10, "status": "downloading", "movie": { "tmdbId": 603 } } ] }""");
    var service = Create(servarr.Object, Mock.Of<ITmdbClient>(), ServarrConfig());

    var requests = new[] { new RequestRecord { Id = id, TmdbId = 603, MediaType = "movie", Status = RequestStatus.Approved } };
    var result = await service.GetStatusesAsync(requests, CancellationToken.None);

    var dto = Assert.Single(result);
    Assert.Equal(id, dto.RequestId);
    Assert.Equal("downloading", dto.State);
    Assert.Equal(90, dto.Percent);
  }

  [Fact]
  public async Task GetStatusesAsync_MatchesSeasonByTvdbAndReportsLeastAdvanced()
  {
    var id = Guid.NewGuid();
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetQueueAsync("http://localhost:8989", "sk", true, It.IsAny<CancellationToken>()))
      .ReturnsAsync("""
      { "records": [
        { "size": 100, "sizeleft": 20, "status": "downloading", "series": { "tvdbId": 81189 }, "episode": { "seasonNumber": 2, "episodeNumber": 1 } },
        { "size": 100, "sizeleft": 90, "status": "downloading", "series": { "tvdbId": 81189 }, "episode": { "seasonNumber": 2, "episodeNumber": 2 } }
      ] }
      """);
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>())).ReturnsAsync(81189);
    var service = Create(servarr.Object, tmdb.Object, ServarrConfig());

    var requests = new[] { new RequestRecord { Id = id, TmdbId = 1396, MediaType = "tv", Season = 2, Status = RequestStatus.Approved } };
    var result = await service.GetStatusesAsync(requests, CancellationToken.None);

    var dto = Assert.Single(result);
    Assert.Equal(10, dto.Percent); // least-advanced episode (sizeleft 90/100)
  }

  [Fact]
  public async Task GetStatusesAsync_BackendNotServarr_ReturnsEmpty()
  {
    var config = ServarrConfig();
    config.DownloadBackend = "webhook";
    var service = Create(Mock.Of<IServarrClient>(), Mock.Of<ITmdbClient>(), config);

    var requests = new[] { new RequestRecord { TmdbId = 603, MediaType = "movie", Status = RequestStatus.Approved } };
    Assert.Empty(await service.GetStatusesAsync(requests, CancellationToken.None));
  }

  [Fact]
  public async Task GetStatusesAsync_SkipsNonApprovedRequests()
  {
    var servarr = new Mock<IServarrClient>();
    var service = Create(servarr.Object, Mock.Of<ITmdbClient>(), ServarrConfig());

    var requests = new[] { new RequestRecord { TmdbId = 603, MediaType = "movie", Status = RequestStatus.Pending } };
    Assert.Empty(await service.GetStatusesAsync(requests, CancellationToken.None));
    servarr.Verify(s => s.GetQueueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task GetStatusesAsync_QueueFailure_IsBestEffort()
  {
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetQueueAsync(It.IsAny<string>(), "rk", false, It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("boom"));
    var service = Create(servarr.Object, Mock.Of<ITmdbClient>(), ServarrConfig());

    var requests = new[] { new RequestRecord { TmdbId = 603, MediaType = "movie", Status = RequestStatus.Approved } };
    Assert.Empty(await service.GetStatusesAsync(requests, CancellationToken.None));
  }
}
