using System;
using System.IO;
using System.Text.Json.Nodes;
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
/// Tests for <see cref="ServarrStalledRecovery"/>.
/// </summary>
public sealed class ServarrStalledRecoveryTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;

  private readonly PluginConfiguration _config = new()
  {
    DownloadBackend = "servarr",
    RecoverStalledDownloads = true,
    StalledRecoveryMinutes = 60,
    SonarrUrl = "http://localhost:8989",
    SonarrApiKey = "sk"
  };

  public ServarrStalledRecoveryTests()
  {
    _store = new JsonRequestStore(_path);
  }

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  [Fact]
  public async Task RecoverAsync_EpisodeRequest_DropsOnlyItsOwnStalledGrab_AndSearchesThatEpisode()
  {
    // Two downloads in the same season: this request's E2 is stuck, another request's E5 is progressing.
    // Recovering E2 must neither blocklist E5's download nor search the whole season.
    var created = await _store.CreateAsync(
      new RequestRecord { TmdbId = 1396, MediaType = "tv", Title = "BB", Season = 1, Episode = 2 },
      CancellationToken.None);
    await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    const string Queue = "{ \"records\": ["
      + " { \"id\": 1, \"downloadId\": \"A\", \"status\": \"downloading\", \"size\": 100, \"sizeleft\": 60, \"series\": { \"tvdbId\": 81189 }, \"episode\": { \"seasonNumber\": 1, \"episodeNumber\": 5 } },"
      + " { \"id\": 2, \"downloadId\": \"B\", \"status\": \"downloading\", \"size\": 100, \"sizeleft\": 90, \"series\": { \"tvdbId\": 81189 }, \"episode\": { \"seasonNumber\": 1, \"episodeNumber\": 2 } } ] }";
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetQueueAsync("http://localhost:8989", "sk", true, It.IsAny<CancellationToken>())).ReturnsAsync(Queue);
    servarr.Setup(s => s.GetSeriesByTvdbAsync("http://localhost:8989", "sk", 81189, It.IsAny<CancellationToken>())).ReturnsAsync(new JsonObject { ["id"] = 7 });
    servarr.Setup(s => s.GetEpisodesAsync("http://localhost:8989", "sk", 7, It.IsAny<CancellationToken>()))
      .ReturnsAsync("[ { \"id\": 12, \"seasonNumber\": 1, \"episodeNumber\": 2 }, { \"id\": 15, \"seasonNumber\": 1, \"episodeNumber\": 5 } ]");
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>())).ReturnsAsync(81189);
    var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    var recovery = new ServarrStalledRecovery(servarr.Object, tmdb.Object, _store, () => _config, NullLogger<ServarrStalledRecovery>.Instance, () => now);

    await recovery.RecoverAsync(CancellationToken.None); // first sighting starts the stall clock
    now = now.AddMinutes(61);
    await recovery.RecoverAsync(CancellationToken.None); // no progress past the threshold: stalled

    servarr.Verify(s => s.DeleteQueueItemAsync("http://localhost:8989", "sk", 2, true, true, It.IsAny<CancellationToken>()), Times.Once);
    servarr.Verify(s => s.DeleteQueueItemAsync(It.IsAny<string>(), It.IsAny<string>(), 1, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    servarr.Verify(
      s => s.CommandAsync("http://localhost:8989", "sk",
        It.Is<JsonObject>(c => c["name"]!.GetValue<string>() == "EpisodeSearch" && c["episodeIds"]![0]!.GetValue<int>() == 12),
        It.IsAny<CancellationToken>()),
      Times.Once);
    servarr.Verify(
      s => s.CommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.Is<JsonObject>(c => c["name"]!.GetValue<string>() == "SeasonSearch"), It.IsAny<CancellationToken>()),
      Times.Never);
  }
}
