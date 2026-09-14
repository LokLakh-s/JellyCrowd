using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="QuotaHoldPromoter"/>: requests held back only by the disk quota resume
/// automatically once the requester is back within quota, while genuine admin-pending requests don't.
/// </summary>
public sealed class QuotaHoldPromoterTests : IDisposable
{
  private const long Gib = 1024L * 1024 * 1024;

  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;
  private readonly PluginConfiguration _config = new()
  {
    DefaultUserQuotaBytes = 10 * Gib,
    EstimatedMovieSizeBytes = 4 * Gib,
    EstimatedEpisodeSizeBytes = 1 * Gib
  };

  private readonly RecordingDispatcher _dispatcher = new();
  private readonly RecordingNotificationService _notifications = new();

  public QuotaHoldPromoterTests()
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

  private QuotaHoldPromoter Create(ILibraryMatcher matcher)
  {
    var quota = new QuotaService(_store, matcher, new FakeActivityStore(), () => _config);
    return new QuotaHoldPromoter(_store, quota, _dispatcher, _notifications, NullLogger<QuotaHoldPromoter>.Instance);
  }

  [Fact]
  public async Task Promote_ResumesHeldRequest_WhenWithinQuota()
  {
    var user = Guid.NewGuid();
    var held = await CreateHeldAsync(user, tmdbId: 1);
    var promoter = Create(new SizeMatcher(0));

    var count = await promoter.PromoteAsync(CancellationToken.None);

    Assert.Equal(1, count);
    var updated = await _store.GetByIdAsync(held.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, updated!.Status);
    Assert.False(updated.HeldForQuota); // flag cleared on promotion
    Assert.Contains(held.Id, _dispatcher.Dispatched); // resumed request is dispatched
    Assert.Contains(NotificationEvent.Approved, _notifications.Events); // requester is told it resumed
  }

  [Fact]
  public async Task Promote_LeavesHeld_WhenStillOverQuota()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 6 * Gib });
    // An available 4 GiB title plus a 4 GiB held movie estimate commit 8 GiB against the 6 GiB quota.
    await CreateAvailableAsync(user, tmdbId: 2);
    var held = await CreateHeldAsync(user, tmdbId: 1);
    var promoter = Create(new SizeMatcher(4 * Gib));

    var count = await promoter.PromoteAsync(CancellationToken.None);

    Assert.Equal(0, count);
    var updated = await _store.GetByIdAsync(held.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Pending, updated!.Status);
    Assert.True(updated.HeldForQuota);
    Assert.Empty(_dispatcher.Dispatched);
  }

  [Fact]
  public async Task Promote_ResumesHeldRequest_OnceSpaceIsFreed()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 6 * Gib });
    var available = await CreateAvailableAsync(user, tmdbId: 2);
    var held = await CreateHeldAsync(user, tmdbId: 1);
    var promoter = Create(new SizeMatcher(4 * Gib));

    // Over quota at first — stays held.
    Assert.Equal(0, await promoter.PromoteAsync(CancellationToken.None));

    // The available title expires / is deleted, freeing its 4 GiB; the held request now fits.
    await _store.DeleteAsync(available.Id, CancellationToken.None);
    var count = await promoter.PromoteAsync(CancellationToken.None);

    Assert.Equal(1, count);
    Assert.Equal(RequestStatus.Approved, (await _store.GetByIdAsync(held.Id, CancellationToken.None))!.Status);
  }

  [Fact]
  public async Task Promote_IgnoresGenuineAdminPending()
  {
    var user = Guid.NewGuid();
    // A normal admin-approval-pending request (not a quota hold) must never be auto-promoted.
    var pending = await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = 3, MediaType = "movie", Title = "Needs admin", Status = RequestStatus.Pending, HeldForQuota = false },
      CancellationToken.None);
    var promoter = Create(new SizeMatcher(0));

    var count = await promoter.PromoteAsync(CancellationToken.None);

    Assert.Equal(0, count);
    Assert.Equal(RequestStatus.Pending, (await _store.GetByIdAsync(pending.Id, CancellationToken.None))!.Status);
    Assert.Empty(_dispatcher.Dispatched);
    Assert.DoesNotContain(NotificationEvent.Approved, _notifications.Events);
  }

  [Fact]
  public async Task Promote_IsIdempotent()
  {
    var user = Guid.NewGuid();
    await CreateHeldAsync(user, tmdbId: 1);
    var promoter = Create(new SizeMatcher(0));

    await promoter.PromoteAsync(CancellationToken.None);
    await promoter.PromoteAsync(CancellationToken.None);

    Assert.Single(_dispatcher.Dispatched); // promoted and dispatched exactly once
  }

  private async Task<RequestRecord> CreateHeldSeasonAsync(Guid user, int tmdbId, int season, int episodes)
    => await _store.CreateAsync(
      new RequestRecord
      {
        UserId = user,
        TmdbId = tmdbId,
        MediaType = "tv",
        Title = "Show " + tmdbId,
        Season = season,
        EstimatedEpisodes = episodes,
        Status = RequestStatus.Pending,
        HeldForQuota = true
      },
      CancellationToken.None);

  [Fact]
  public async Task Promote_ReleasesWhatFits_AndDoesNotDeadlockOnItsOwnHolds()
  {
    // The reported case: two 11-episode season requests, nothing on disk, a 30 GiB quota and a 1.5 GiB
    // per-episode estimate. Together they reserve 33 GiB. Counting held requests in the footprint that
    // decides whether they may resume made that footprint 33 GiB for ever: neither could ever be released
    // and the user saw a quota reading of zero while being told the quota was full.
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 30 * Gib });
    _config.EstimatedEpisodeSizeBytes = 3 * Gib / 2;
    var first = await CreateHeldSeasonAsync(user, tmdbId: 7, season: 3, episodes: 11);
    var second = await CreateHeldSeasonAsync(user, tmdbId: 7, season: 4, episodes: 11);
    var promoter = Create(new SizeMatcher(0));

    var count = await promoter.PromoteAsync(CancellationToken.None);

    // Exactly one is released — the oldest — and the other keeps waiting rather than over-committing.
    Assert.Equal(1, count);
    Assert.Equal(RequestStatus.Approved, (await _store.GetByIdAsync(first.Id, CancellationToken.None))!.Status);
    var stillHeld = await _store.GetByIdAsync(second.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Pending, stillHeld!.Status);
    Assert.True(stillHeld.HeldForQuota);
  }

  [Fact]
  public async Task Promote_IsStable_WhenRunAgainWithNothingFreed()
  {
    // A second sweep must not release the one that still does not fit (which the dispatcher would then
    // hold straight back), and must not re-release the one already running.
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 30 * Gib });
    _config.EstimatedEpisodeSizeBytes = 3 * Gib / 2;
    await CreateHeldSeasonAsync(user, tmdbId: 7, season: 3, episodes: 11);
    await CreateHeldSeasonAsync(user, tmdbId: 7, season: 4, episodes: 11);
    var promoter = Create(new SizeMatcher(0));

    Assert.Equal(1, await promoter.PromoteAsync(CancellationToken.None));
    Assert.Equal(0, await promoter.PromoteAsync(CancellationToken.None));
  }

  [Fact]
  public async Task Promote_SkipsAnOversizedHold_WithoutStarvingTheOnesBehindIt()
  {
    // A request that cannot fit whatever happens must not block the smaller ones queued after it.
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 10 * Gib });
    _config.EstimatedEpisodeSizeBytes = 1 * Gib;
    var oversized = await CreateHeldSeasonAsync(user, tmdbId: 1, season: 1, episodes: 40);
    var small = await CreateHeldSeasonAsync(user, tmdbId: 2, season: 1, episodes: 2);
    var promoter = Create(new SizeMatcher(0));

    Assert.Equal(1, await promoter.PromoteAsync(CancellationToken.None));
    Assert.Equal(RequestStatus.Pending, (await _store.GetByIdAsync(oversized.Id, CancellationToken.None))!.Status);
    Assert.Equal(RequestStatus.Approved, (await _store.GetByIdAsync(small.Id, CancellationToken.None))!.Status);
  }

  private async Task<RequestRecord> CreateHeldAsync(Guid user, int tmdbId)
    => await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = tmdbId, MediaType = "movie", Title = "Held " + tmdbId, Status = RequestStatus.Pending, HeldForQuota = true },
      CancellationToken.None);

  private async Task<RequestRecord> CreateAvailableAsync(Guid user, int tmdbId)
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = tmdbId, MediaType = "movie", Title = "Owned " + tmdbId },
      CancellationToken.None);
    await _store.MarkAvailableAsync(created.Id, "item-" + tmdbId, CancellationToken.None);
    return created;
  }

  private sealed class FakeActivityStore : IUserActivityStore
  {
    public UserActivity Get(Guid userId) => new() { UserId = userId };

    public IReadOnlyList<UserActivity> GetAll() => Array.Empty<UserActivity>();

    public void RecordPlayback(Guid userId, DateTime nowUtc, double minutes)
    {
    }

    public void Update(UserActivity activity)
    {
    }
  }

  private sealed class SizeMatcher : ILibraryMatcher
  {
    private readonly long _size;

    public SizeMatcher(long size) => _size = size;

    public bool Exists(string mediaType, int tmdbId) => _size > 0;

    public string? FindItemId(string mediaType, int tmdbId) => _size > 0 ? "x" : null;

    public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode) => _size > 0 ? "x" : null;

    public string? FindSeasonItemId(int seriesTmdbId, int season) => _size > 0 ? "x" : null;

    public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode) => _size;

    public long GetSizeBytes(string mediaType, int tmdbId) => _size;

    public IReadOnlyList<LibraryMediaItem> ListLibraryMedia() => Array.Empty<LibraryMediaItem>();
  }

  private sealed class RecordingDispatcher : IDownloadDispatcher
  {
    public List<Guid> Dispatched { get; } = new();

    public Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken)
    {
      Dispatched.Add(request.Id);
      return Task.FromResult(true);
    }

    public Task DispatchDueAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TestActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> PurgeAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task RetryStuckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RescanAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> RetryAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(true);
  }
}
