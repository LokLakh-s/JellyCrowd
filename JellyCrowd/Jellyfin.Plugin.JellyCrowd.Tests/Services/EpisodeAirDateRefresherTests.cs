using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="EpisodeAirDateRefresher"/> using a real <see cref="JsonRequestStore"/>.
/// </summary>
public sealed class EpisodeAirDateRefresherTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;
  private readonly StubTmdbClient _tmdb = new();

  public EpisodeAirDateRefresherTests()
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

  private EpisodeAirDateRefresher CreateRefresher()
    => new(_store, _tmdb, NullLogger<EpisodeAirDateRefresher>.Instance);

  private async Task<RequestRecord> SeedAsync(int? episode, string? releaseDate, RequestStatus status = RequestStatus.Approved, DateTime? desiredAt = null)
  {
    var created = await _store.CreateAsync(
      new RequestRecord { TmdbId = 288385, MediaType = "tv", Title = "Paolo", Season = 1, Episode = episode, ReleaseDate = releaseDate, DesiredAt = desiredAt },
      CancellationToken.None);
    return (await _store.UpdateStatusAsync(created.Id, status, Guid.NewGuid(), CancellationToken.None))!;
  }

  [Fact]
  public async Task RefreshAsync_EpisodeKeptOnTheSeriesPremiere_IsDeferredToItsAirDate()
  {
    // The reported case: episodes 2-7 requested before TMDB had their dates, all left on the premiere.
    var airDate = DateTime.UtcNow.Date.AddDays(8);
    var iso = airDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    _tmdb.EpisodesBySeason[1] = new[] { new Episode { SeasonNumber = 1, EpisodeNumber = 3, AirDate = iso } };
    var request = await SeedAsync(3, "2026-09-25", desiredAt: new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc));

    var rescheduled = await CreateRefresher().RefreshAsync(CancellationToken.None);

    Assert.Equal(1, rescheduled);
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Equal(iso, stored!.ReleaseDate);
    Assert.Equal(airDate, stored.DesiredAt);
  }

  [Fact]
  public async Task RefreshAsync_LeavesUnchangedAndUnknownDatesAlone()
  {
    _tmdb.EpisodesBySeason[1] = new[]
    {
      new Episode { SeasonNumber = 1, EpisodeNumber = 2, AirDate = "2030-01-08" },
      new Episode { SeasonNumber = 1, EpisodeNumber = 4, AirDate = null }
    };
    await SeedAsync(2, "2030-01-08", desiredAt: new DateTime(2030, 1, 8, 0, 0, 0, DateTimeKind.Utc));
    await SeedAsync(4, null);

    Assert.Equal(0, await CreateRefresher().RefreshAsync(CancellationToken.None));
  }

  [Fact]
  public async Task RefreshAsync_IgnoresSeasonRequestsAndFulfilledOnes()
  {
    _tmdb.EpisodesBySeason[1] = new[] { new Episode { SeasonNumber = 1, EpisodeNumber = 1, AirDate = "2030-01-01" } };
    await SeedAsync(null, "2026-09-25");
    await SeedAsync(1, "2026-09-25", RequestStatus.Available);

    Assert.Equal(0, await CreateRefresher().RefreshAsync(CancellationToken.None));
  }

  [Fact]
  public async Task RefreshAsync_TmdbUnreachable_ChangesNothing()
  {
    var request = await SeedAsync(3, "2026-09-25");
    _tmdb.EpisodesUnavailable = true;

    Assert.Equal(0, await CreateRefresher().RefreshAsync(CancellationToken.None));
    Assert.Equal("2026-09-25", (await _store.GetByIdAsync(request.Id, CancellationToken.None))!.ReleaseDate);
  }
}
