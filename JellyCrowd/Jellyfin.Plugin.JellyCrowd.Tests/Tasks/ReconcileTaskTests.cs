using System;
using System.IO;
using System.Linq;
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
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
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
    var reconciler = new RequestReconciler(_store, new StubMatcher(true), new NoopNotificationService(), new RecordingDispatcher(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    var updated = await _store.GetByIdAsync(id, CancellationToken.None);
    Assert.Equal(RequestStatus.Available, updated!.Status);
  }

  [Fact]
  public async Task Execute_LeavesApproved_WhenNotInLibrary()
  {
    var id = await SeedApprovedAsync();
    var reconciler = new RequestReconciler(_store, new StubMatcher(false), new NoopNotificationService(), new RecordingDispatcher(), NullLogger<RequestReconciler>.Instance);

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
    var reconciler = new RequestReconciler(_store, matcher, new NoopNotificationService(), new RecordingDispatcher(), NullLogger<RequestReconciler>.Instance);

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
    var reconciler = new RequestReconciler(_store, matcher, new NoopNotificationService(), new RecordingDispatcher(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    var updated = await _store.GetByIdAsync(created.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Available, updated!.Status);
  }

  [Fact]
  public async Task Execute_GrantsOwnershipToAllRequesters_IncludingPending()
  {
    // Two people requested the same title; one approved, one still pending. Once it's in the library
    // both must own it (N31) — the pending requester shouldn't be left out.
    var approved = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 5, MediaType = "movie", Title = "X" },
      CancellationToken.None);
    await _store.UpdateStatusAsync(approved.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    var pending = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 5, MediaType = "movie", Title = "X" },
      CancellationToken.None); // stays Pending

    var reconciler = new RequestReconciler(_store, new StubMatcher(true), new NoopNotificationService(), new RecordingDispatcher(), NullLogger<RequestReconciler>.Instance);
    await reconciler.ReconcileAsync(CancellationToken.None);

    Assert.Equal(RequestStatus.Available, (await _store.GetByIdAsync(approved.Id, CancellationToken.None))!.Status);
    Assert.Equal(RequestStatus.Available, (await _store.GetByIdAsync(pending.Id, CancellationToken.None))!.Status);
  }

  [Fact]
  public async Task Execute_RevertsAvailableToApproved_WhenMediaDisappears()
  {
    // An Available request whose media is no longer in the library (deleted externally) and isn't flagged
    // for deletion is reverted to Approved so it gets re-fetched.
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 5, MediaType = "movie", Title = "X" },
      CancellationToken.None);
    await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    await _store.MarkAvailableAsync(created.Id, "item-x", CancellationToken.None);

    var reconciler = new RequestReconciler(_store, new StubMatcher(false), new NoopNotificationService(), new RecordingDispatcher(), NullLogger<RequestReconciler>.Instance);
    await reconciler.ReconcileAsync(CancellationToken.None);

    Assert.Equal(RequestStatus.Approved, (await _store.GetByIdAsync(created.Id, CancellationToken.None))!.Status);
  }

  [Fact]
  public async Task Execute_RescansBackend_WhenDispatchedRequestBecomesAvailable()
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 7, MediaType = "movie", Title = "Y" }, CancellationToken.None);
    await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    await _store.MarkDispatchedAsync(created.Id, DateTime.UtcNow, CancellationToken.None);
    var dispatcher = new RecordingDispatcher();
    var reconciler = new RequestReconciler(_store, new StubMatcher(true), new NoopNotificationService(), dispatcher, NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    Assert.NotNull(dispatcher.Rescanned);
    Assert.Equal(created.Id, dispatcher.Rescanned!.Id);
  }

  [Fact]
  public async Task Execute_DoesNotRescan_WhenRequestWasNotDispatched()
  {
    await SeedApprovedAsync(); // no DispatchedAt
    var dispatcher = new RecordingDispatcher();
    var reconciler = new RequestReconciler(_store, new StubMatcher(true), new NoopNotificationService(), dispatcher, NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    Assert.Null(dispatcher.Rescanned);
  }

  [Fact]
  public async Task Execute_GroupsAvailableNotifications_ForEpisodesOfTheSameSeason()
  {
    // A season dropping several episodes at once (the Rick & Morty S9 case) must produce ONE grouped
    // "now available" notification, not one per episode.
    var user = Guid.NewGuid();
    for (var ep = 1; ep <= 6; ep++)
    {
      var created = await _store.CreateAsync(
        new RequestRecord { UserId = user, TmdbId = 42, MediaType = "tv", Title = "Rick and Morty", Season = 9, Episode = ep },
        CancellationToken.None);
      await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    }

    var notifier = new RecordingNotificationService();
    var reconciler = new RequestReconciler(_store, new StubMatcher(true), notifier, new RecordingDispatcher(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    Assert.Equal(new[] { 6 }, notifier.AvailableBatches); // one batched call grouping all six episodes
  }

  [Fact]
  public async Task Execute_SeparatesAvailableBatches_ByRequesterAndSeason()
  {
    // Groups never merge across requester or season: Alice S1 (2 eps), Alice S2 (1), Bob S1 (1) → 3 groups.
    async Task SeedEpisodeAsync(Guid user, int season, int episode)
    {
      var created = await _store.CreateAsync(
        new RequestRecord { UserId = user, TmdbId = 42, MediaType = "tv", Title = "Show", Season = season, Episode = episode },
        CancellationToken.None);
      await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    }

    var alice = Guid.NewGuid();
    var bob = Guid.NewGuid();
    await SeedEpisodeAsync(alice, 1, 1);
    await SeedEpisodeAsync(alice, 1, 2);
    await SeedEpisodeAsync(alice, 2, 1);
    await SeedEpisodeAsync(bob, 1, 1);

    var notifier = new RecordingNotificationService();
    var reconciler = new RequestReconciler(_store, new StubMatcher(true), notifier, new RecordingDispatcher(), NullLogger<RequestReconciler>.Instance);

    await reconciler.ReconcileAsync(CancellationToken.None);

    Assert.Equal(3, notifier.AvailableBatches.Count);          // one per (user, season) group
    Assert.Equal(new[] { 1, 1, 2 }, notifier.AvailableBatches.OrderBy(n => n).ToArray()); // Alice S1 has two episodes
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

    public string? FindSeasonItemId(int seriesTmdbId, int season) => _result ? "x" : null;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;

    public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode) => 0;

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

    public string? FindSeasonItemId(int seriesTmdbId, int season) => _seriesFound ? "season" : null;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;

    public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode) => 0;

    public System.Collections.Generic.IReadOnlyList<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem> ListLibraryMedia() => System.Array.Empty<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem>();
  }

  private sealed class RecordingDispatcher : IDownloadDispatcher
  {
    public RequestRecord? Rescanned { get; private set; }

    public Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task DispatchDueAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TestActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> PurgeAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> RetryAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task RetryStuckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RescanAsync(RequestRecord request, CancellationToken cancellationToken)
    {
      Rescanned = request;
      return Task.CompletedTask;
    }
  }

  private sealed class NoopNotificationService : INotificationService
  {
    public Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task NotifyAvailableBatchAsync(System.Collections.Generic.IReadOnlyList<RequestRecord> requests, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task NotifyPersonalAsync(Guid userId, PersonalNotifyKind kind, string title, string subject, string body, string? posterPath, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task SendTestAsync(string channel, CancellationToken cancellationToken) => Task.CompletedTask;
  }
}
