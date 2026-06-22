using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Tasks;

/// <summary>
/// Tests for <see cref="RequestReconciler"/>.
/// </summary>
public sealed class ReconcileTaskTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;

  public ReconcileTaskTests()
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
  public async Task Execute_MarksApprovedAvailable_WhenInLibrary()
  {
    var id = await SeedApprovedAsync();
    var reconciler = new RequestReconciler(_store, new StubMatcher(true), new NoopNotificationService(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    var updated = await _store.GetByIdAsync(id, CancellationToken.None);
    Assert.Equal(RequestStatus.Available, updated!.Status);
  }

  [Fact]
  public async Task Execute_LeavesApproved_WhenNotInLibrary()
  {
    var id = await SeedApprovedAsync();
    var reconciler = new RequestReconciler(_store, new StubMatcher(false), new NoopNotificationService(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    var updated = await _store.GetByIdAsync(id, CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, updated!.Status);
  }

  [Fact]
  public async Task Execute_LeavesEpisodeApproved_WhenOnlySeriesPresentButEpisodeMissing()
  {
    // Regression: a per-episode TV request must not flip to Available just because the series exists
    // (only ep.1 imported should not make all 10 episode requests Available).
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 99, MediaType = "tv", Title = "HotD", Season = 3, Episode = 5 },
      CancellationToken.None);
    await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);

    // Series-level match succeeds, but the specific episode is absent.
    var matcher = new EpisodeStubMatcher(seriesFound: true, episodeFound: false);
    var reconciler = new RequestReconciler(_store, matcher, new NoopNotificationService(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    var updated = await _store.GetByIdAsync(created.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, updated!.Status);
  }

  [Fact]
  public async Task Execute_MarksEpisodeAvailable_WhenEpisodePresent()
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 99, MediaType = "tv", Title = "HotD", Season = 3, Episode = 1 },
      CancellationToken.None);
    await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);

    var matcher = new EpisodeStubMatcher(seriesFound: true, episodeFound: true);
    var reconciler = new RequestReconciler(_store, matcher, new NoopNotificationService(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    var updated = await _store.GetByIdAsync(created.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Available, updated!.Status);
  }

  private async Task<Guid> SeedApprovedAsync()
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 5, MediaType = "movie", Title = "X" },
      CancellationToken.None);
    await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    return created.Id;
  }

  private sealed class StubMatcher : ILibraryMatcher
  {
    private readonly bool _result;

    public StubMatcher(bool result) => _result = result;

    public bool Exists(string mediaType, int tmdbId) => _result;

    public string? FindItemId(string mediaType, int tmdbId) => _result ? "x" : null;

    public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode) => _result ? "x" : null;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;

    public System.Collections.Generic.IReadOnlyList<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem> ListLibraryMedia() => System.Array.Empty<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem>();
  }

  private sealed class EpisodeStubMatcher : ILibraryMatcher
  {
    private readonly bool _seriesFound;
    private readonly bool _episodeFound;

    public EpisodeStubMatcher(bool seriesFound, bool episodeFound)
    {
      _seriesFound = seriesFound;
      _episodeFound = episodeFound;
    }

    public bool Exists(string mediaType, int tmdbId) => _seriesFound;

    public string? FindItemId(string mediaType, int tmdbId) => _seriesFound ? "series" : null;

    public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode) => _episodeFound ? "episode" : null;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;

    public System.Collections.Generic.IReadOnlyList<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem> ListLibraryMedia() => System.Array.Empty<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem>();
  }

  private sealed class NoopNotificationService : INotificationService
  {
    public Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task NotifyPersonalAsync(Guid userId, PersonalNotifyKind kind, string title, string subject, string body, string? posterPath, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task SendTestAsync(string channel, CancellationToken cancellationToken) => Task.CompletedTask;
  }
}
