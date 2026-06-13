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

  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
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

  private QuotaService Create(ILibraryMatcher matcher) => new(_store, matcher, () => _config);

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
  public async Task CanRequestAsync_FalseWhenEstimateExceedsQuota()
  {
    var user = Guid.NewGuid();
    _config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, QuotaBytes = 2 * Gib });
    var service = Create(new SizeMatcher(0));

    // A movie estimate (4 GiB) alone exceeds the 2 GiB quota.
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

  private sealed class SizeMatcher : ILibraryMatcher
  {
    private readonly long _size;

    public SizeMatcher(long size) => _size = size;

    public bool Exists(string mediaType, int tmdbId) => _size > 0;

    public string? FindItemId(string mediaType, int tmdbId) => _size > 0 ? "x" : null;

    public long GetSizeBytes(string mediaType, int tmdbId) => _size;
  }
}
