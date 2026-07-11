using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="QuotaService"/>.
/// </summary>
public sealed class QuotaServiceTests : IDisposable
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

  public QuotaServiceTests()
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

  private QuotaService Create(ILibraryMatcher matcher) => Create(matcher, new FakeActivityStore());

  private QuotaService Create(ILibraryMatcher matcher, IUserActivityStore activityStore) => new(_store, matcher, activityStore, () => _config);

  [Fact]
  public void GetQuotaBytes_UsesOverrideThenDefault()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 2 * Gib });
    var service = Create(new SizeMatcher(0));

    Assert.Equal(2 * Gib, service.GetQuotaBytes(user));
    Assert.Equal(10 * Gib, service.GetQuotaBytes(Guid.NewGuid()));
  }

  [Fact]
  public async Task GetUsageAsync_SumsAvailableSizes()
  {
    var user = Guid.NewGuid();
    await SeedAsync(user, RequestStatus.Available);
    var service = Create(new SizeMatcher(3 * Gib));

    var info = await service.GetUsageAsync(user, CancellationToken.None);

    Assert.Equal(3 * Gib, info.UsedBytes);
    Assert.Equal(10 * Gib, info.QuotaBytes);
    Assert.False(info.Unlimited);
  }

  [Fact]
  public async Task GetUsageAsync_IgnoresInFlightRequests()
  {
    var user = Guid.NewGuid();
    // A pending request has no on-disk size yet; displayed usage must stay at 0 until it lands.
    // Its theoretical footprint only gates CanRequestAsync, never the displayed figure.
    await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = 7, MediaType = "movie", Title = "P" },
      CancellationToken.None);
    var service = Create(new SizeMatcher(0));

    var info = await service.GetUsageAsync(user, CancellationToken.None);

    Assert.Equal(0, info.UsedBytes);
  }

  [Fact]
  public async Task GetUsageAsync_CountsSharedTitleOnce()
  {
    var user = Guid.NewGuid();
    // Two available requests for the same title (shared ownership / re-claim) must count its size once.
    var a = await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "X" }, CancellationToken.None);
    await _store.UpdateStatusAsync(a.Id, RequestStatus.Available, Guid.NewGuid(), CancellationToken.None);
    var b = await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "X" }, CancellationToken.None);
    await _store.UpdateStatusAsync(b.Id, RequestStatus.Available, Guid.NewGuid(), CancellationToken.None);
    var service = Create(new SizeMatcher(3 * Gib));

    var info = await service.GetUsageAsync(user, CancellationToken.None);

    Assert.Equal(3 * Gib, info.UsedBytes); // counted once, not 6 GiB
  }

  [Fact]
  public async Task AdaptiveQuota_ScalesQuotaAndSurfacesTier()
  {
    var user = Guid.NewGuid();
    _config.AdaptiveQuotaEnabled = true;
    _config.AdaptiveCeilingPercent = 200;
    _config.AdaptiveFloorPercent = 40;
    var store = new FakeActivityStore { Preset = new UserActivity { UserId = user, Tier = AdaptiveTier.Ceiling } };
    var service = Create(new SizeMatcher(0), store);

    Assert.Equal(20 * Gib, service.GetQuotaBytes(user)); // 200% of the 10 GiB default

    var info = await service.GetUsageAsync(user, CancellationToken.None);
    Assert.True(info.AdaptiveEnabled);
    Assert.Equal("ceiling", info.Tier);
    Assert.False(info.InProbation);
  }

  [Fact]
  public async Task AdaptiveQuota_Probation_FreezesQuotaAndReportsProbation()
  {
    var user = Guid.NewGuid();
    _config.AdaptiveQuotaEnabled = true;
    _config.AdaptiveProbationDays = 14;
    var frozen = 7 * Gib;
    var store = new FakeActivityStore
    {
      Preset = new UserActivity { UserId = user, Tier = AdaptiveTier.Ceiling, ProbationStartUtc = DateTime.UtcNow, FrozenQuotaBytes = frozen }
    };
    var service = Create(new SizeMatcher(0), store);

    Assert.Equal(frozen, service.GetQuotaBytes(user)); // frozen value, not a tier %
    var info = await service.GetUsageAsync(user, CancellationToken.None);
    Assert.True(info.InProbation);
    Assert.NotNull(info.ProbationEndsUtc);
  }

  [Fact]
  public async Task CanRequestAsync_FalseWhenEstimateExceedsQuota()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 2 * Gib });
    var service = Create(new SizeMatcher(0));

    // A movie estimate (4 GiB) alone exceeds the 2 GiB quota.
    Assert.False(await service.CanRequestAsync(user, "movie", CancellationToken.None));
  }

  [Fact]
  public async Task CanRequestAsync_CountsInFlightFootprintEvenThoughUsageHidesIt()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 6 * Gib });
    // Two in-flight movie requests (2 x 4 GiB estimate) already commit 8 GiB against the 6 GiB quota,
    // so a third must be held — even though displayed usage stays at 0 until anything lands.
    await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "A" }, CancellationToken.None);
    await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 2, MediaType = "movie", Title = "B" }, CancellationToken.None);
    var service = Create(new SizeMatcher(0));

    Assert.Equal(0, (await service.GetUsageAsync(user, CancellationToken.None)).UsedBytes);
    Assert.False(await service.CanRequestAsync(user, "movie", CancellationToken.None));
  }

  [Fact]
  public async Task CanRequestAsync_TrueWhenItFits()
  {
    var service = Create(new SizeMatcher(0));

    // Default quota 10 GiB, one movie estimate 4 GiB.
    Assert.True(await service.CanRequestAsync(Guid.NewGuid(), "movie", CancellationToken.None));
  }

  [Fact]
  public async Task CanRequestAsync_TrueWhenUnlimited()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 0 });
    var service = Create(new SizeMatcher(999 * Gib));

    Assert.True(await service.CanRequestAsync(user, "movie", CancellationToken.None));
  }

  [Fact]
  public async Task IsWithinQuotaAsync_TrueWhenExistingFootprintFits()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 6 * Gib });
    // One in-flight movie commits 4 GiB against the 6 GiB quota — it fits, and nothing new is added.
    await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "A" }, CancellationToken.None);
    var service = Create(new SizeMatcher(0));

    Assert.True(await service.IsWithinQuotaAsync(user, CancellationToken.None));
  }

  [Fact]
  public async Task IsWithinQuotaAsync_FalseWhenExistingFootprintExceedsQuota()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 6 * Gib });
    // Two in-flight movies already commit 8 GiB > the 6 GiB quota.
    await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "A" }, CancellationToken.None);
    await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 2, MediaType = "movie", Title = "B" }, CancellationToken.None);
    var service = Create(new SizeMatcher(0));

    Assert.False(await service.IsWithinQuotaAsync(user, CancellationToken.None));
  }

  [Fact]
  public async Task IsWithinQuotaAsync_TrueWhenUnlimited()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 0 });
    var service = Create(new SizeMatcher(999 * Gib));

    Assert.True(await service.IsWithinQuotaAsync(user, CancellationToken.None));
  }

  [Fact]
  public async Task IsWithinQuotaAsync_IgnoresNotYetReleasedRequests()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 6 * Gib });
    // One released movie commits 4 GiB. A second, not-yet-released movie (DesiredAt in the future)
    // must NOT reserve quota until its release date — so the user stays within the 6 GiB quota,
    // whereas two released movies (8 GiB) would exceed it (see the test above).
    await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "A" }, CancellationToken.None);
    await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = 2, MediaType = "movie", Title = "B", DesiredAt = DateTime.UtcNow.AddDays(30) },
      CancellationToken.None);
    var service = Create(new SizeMatcher(0));

    Assert.True(await service.IsWithinQuotaAsync(user, CancellationToken.None));
  }

  [Fact]
  public async Task CanRequestAsync_IgnoresNotYetReleasedInFlightRequests()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 6 * Gib });
    // Two not-yet-released movies are in flight; they don't reserve quota yet, so a new released
    // request (4 GiB) still fits under the 6 GiB quota. This is the reported bug: a batch of
    // unreleased requests must not block a released film that fits on disk.
    await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "A", DesiredAt = DateTime.UtcNow.AddDays(30) },
      CancellationToken.None);
    await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = 2, MediaType = "movie", Title = "B", DesiredAt = DateTime.UtcNow.AddDays(30) },
      CancellationToken.None);
    var service = Create(new SizeMatcher(0));

    Assert.True(await service.CanRequestAsync(user, "movie", CancellationToken.None));
  }

  private async Task SeedAsync(Guid user, RequestStatus status)
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "X" },
      CancellationToken.None);
    if (status != RequestStatus.Pending)
    {
      await _store.UpdateStatusAsync(created.Id, status, Guid.NewGuid(), CancellationToken.None);
    }
  }

  private sealed class FakeActivityStore : IUserActivityStore
  {
    public UserActivity? Preset { get; set; }

    public UserActivity Get(Guid userId) => Preset ?? new UserActivity { UserId = userId };

    public System.Collections.Generic.IReadOnlyList<UserActivity> GetAll() => System.Array.Empty<UserActivity>();

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

    public System.Collections.Generic.IReadOnlyList<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem> ListLibraryMedia() => System.Array.Empty<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem>();
  }
}
