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
/// Tests for <see cref="MovieReleaseRefresher"/> using a real <see cref="JsonRequestStore"/>.
/// </summary>
public sealed class MovieReleaseRefresherTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;
  private readonly StubTmdbClient _tmdb = new();

  public MovieReleaseRefresherTests()
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

  private MovieReleaseRefresher CreateRefresher() => new(_store, _tmdb, NullLogger<MovieReleaseRefresher>.Instance);

  private async Task<RequestRecord> SeedAsync(int tmdbId, DateTime desiredAt, RequestStatus status = RequestStatus.Approved)
  {
    var created = await _store.CreateAsync(
      new RequestRecord { TmdbId = tmdbId, MediaType = "movie", Title = "Movie " + tmdbId, DesiredAt = desiredAt },
      CancellationToken.None);
    return (await _store.UpdateStatusAsync(created.Id, status, Guid.NewGuid(), CancellationToken.None))!;
  }

  [Fact]
  public async Task RefreshAsync_MovieStillInCinemas_WaitsForItsHomeRelease()
  {
    // The reported case: sent at its cinema date, searched for two weeks, then reported not found.
    var digital = DateTime.UtcNow.Date.AddDays(23);
    _tmdb.MovieReleases[1165369] = new MovieRelease { Status = "Released", Theatrical = DateTime.UtcNow.Date.AddDays(-100), Digital = digital };
    var request = await SeedAsync(1165369, DateTime.UtcNow.Date.AddDays(-32));

    var rescheduled = await CreateRefresher().RefreshAsync(CancellationToken.None);

    Assert.Equal(1, rescheduled);
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Equal(digital, stored!.DesiredAt);
    Assert.True(DownloadEligibility.IsAwaitingRelease(stored, DateTime.UtcNow));
  }

  [Fact]
  public async Task RefreshAsync_MovieWithNoDate_IsFlagged_ThenReleasedOnceItHasOne()
  {
    var request = await SeedAsync(1783806, DateTime.UtcNow.Date.AddDays(-3));
    _tmdb.MovieReleases[1783806] = new MovieRelease { Status = "Post Production" };
    await CreateRefresher().RefreshAsync(CancellationToken.None);
    Assert.True((await _store.GetByIdAsync(request.Id, CancellationToken.None))!.AwaitingReleaseDate);

    var digital = DateTime.UtcNow.Date.AddDays(40);
    _tmdb.MovieReleases[1783806] = new MovieRelease { Status = "Post Production", Digital = digital };
    await CreateRefresher().RefreshAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.False(stored!.AwaitingReleaseDate);
    Assert.Equal(digital, stored.DesiredAt);
  }

  [Fact]
  public async Task RefreshAsync_MovieAlreadyOut_IsLeftAlone()
  {
    _tmdb.MovieReleases[286491] = new MovieRelease { Status = "Released", Theatrical = new DateTime(2014, 7, 26, 0, 0, 0, DateTimeKind.Utc) };
    var desired = DateTime.UtcNow.Date.AddDays(-48);
    var request = await SeedAsync(286491, desired);

    Assert.Equal(0, await CreateRefresher().RefreshAsync(CancellationToken.None));
    Assert.Equal(desired, (await _store.GetByIdAsync(request.Id, CancellationToken.None))!.DesiredAt);
  }

  [Fact]
  public async Task RefreshAsync_AvailableRequests_AreNotTouched()
  {
    _tmdb.MovieReleases[5] = new MovieRelease { Status = "Released", Digital = DateTime.UtcNow.Date.AddDays(10) };
    await SeedAsync(5, DateTime.UtcNow.Date.AddDays(-5), RequestStatus.Available);

    Assert.Equal(0, await CreateRefresher().RefreshAsync(CancellationToken.None));
  }
}
