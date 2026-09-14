using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
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
/// Tests for <see cref="QuotaController"/> using a real <see cref="JsonRequestStore"/> and fakes.
/// </summary>
public sealed class QuotaControllerTests : IDisposable
{
  private static readonly Guid User = Guid.NewGuid();
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;

  public QuotaControllerTests() => _store = new JsonRequestStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private QuotaController CreateController(bool isAdmin = false, long reservedBytes = 0)
    => new(new FakeQuotaService(reservedBytes), new FakeUserAccessor(isAdmin), _store, new FakeMatcher())
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

  [Fact]
  public async Task Me_HidesTheReservedFootprint_FromRegularUsers()
  {
    // The footprint is a deliberately pessimistic upper bound, not space taken: shown to a user it reads as
    // a fuller quota than they really have.
    var result = await CreateController(isAdmin: false, reservedBytes: 20L << 30).Me(CancellationToken.None);

    var info = Assert.IsType<QuotaInfo>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(0, info.ReservedBytes);
    Assert.Equal(7, info.UsedBytes); // the rest of the reading is untouched
  }

  [Fact]
  public async Task Me_ShowsTheReservedFootprint_ToAdministrators()
  {
    var result = await CreateController(isAdmin: true, reservedBytes: 20L << 30).Me(CancellationToken.None);

    var info = Assert.IsType<QuotaInfo>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(20L << 30, info.ReservedBytes);
  }

  [Fact]
  public async Task MyMedia_ReturnsAvailableItemsWithSizes()
  {
    var avail = await _store.CreateAsync(new RequestRecord { UserId = User, TmdbId = 603, MediaType = "movie", Title = "The Matrix" }, CancellationToken.None);
    await _store.MarkAvailableAsync(avail.Id, "abc", CancellationToken.None);
    await _store.CreateAsync(new RequestRecord { UserId = User, TmdbId = 1, MediaType = "movie", Title = "Pending" }, CancellationToken.None);

    var result = await CreateController().MyMedia(CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var media = Assert.IsAssignableFrom<IReadOnlyList<MediaUsageDto>>(ok.Value);
    var dto = Assert.Single(media);
    Assert.Equal(603, dto.TmdbId);
    Assert.Equal("abc", dto.JellyfinItemId);
    Assert.Equal(123456, dto.SizeBytes);
  }

  [Fact]
  public async Task MyMedia_ExcludesOtherUsers()
  {
    var other = await _store.CreateAsync(new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 603, MediaType = "movie", Title = "X" }, CancellationToken.None);
    await _store.MarkAvailableAsync(other.Id, "z", CancellationToken.None);

    var result = await CreateController().MyMedia(CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<MediaUsageDto>>(ok.Value));
  }

  [Fact]
  public async Task MyMedia_OwnerCount_KeepsFlaggedOwnersUntilDeletionCompletes()
  {
    // The test user and another user own the same movie.
    var mine = await _store.CreateAsync(new RequestRecord { UserId = User, TmdbId = 700, MediaType = "movie", Title = "Shared" }, CancellationToken.None);
    await _store.MarkAvailableAsync(mine.Id, "a", CancellationToken.None);
    var other = await _store.CreateAsync(new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 700, MediaType = "movie", Title = "Shared" }, CancellationToken.None);
    await _store.MarkAvailableAsync(other.Id, "a", CancellationToken.None);

    Assert.Equal(2, await OwnerCountOfMine());

    // The other owner requests deletion — ownership must NOT drop yet (they can still cancel during the grace).
    await _store.RequestDeletionAsync(other.Id, other.UserId, CancellationToken.None);
    Assert.Equal(2, await OwnerCountOfMine());

    // Only when the deletion actually completes (its request is removed) does the count drop.
    await _store.DeleteAsync(other.Id, CancellationToken.None);
    Assert.Equal(1, await OwnerCountOfMine());
  }

  private async Task<int> OwnerCountOfMine()
  {
    var result = await CreateController().MyMedia(CancellationToken.None);
    var media = Assert.IsAssignableFrom<IReadOnlyList<MediaUsageDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
    return Assert.Single(media).OwnerCount;
  }

  private sealed class FakeMatcher : ILibraryMatcher
  {
    public bool Exists(string mediaType, int tmdbId) => true;

    public string? FindItemId(string mediaType, int tmdbId) => null;

    public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode) => null;

    public string? FindSeasonItemId(int seriesTmdbId, int season) => null;

    public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode) => 123456;

    public long GetSizeBytes(string mediaType, int tmdbId) => 123456;

    public System.Collections.Generic.IReadOnlyList<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem> ListLibraryMedia() => System.Array.Empty<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem>();
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    private readonly bool _isAdmin;

    public FakeUserAccessor(bool isAdmin) => _isAdmin = isAdmin;

    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(_isAdmin);
  }

  private sealed class FakeQuotaService : IQuotaService
  {
    private readonly long _reservedBytes;

    public FakeQuotaService(long reservedBytes) => _reservedBytes = reservedBytes;

    public long GetQuotaBytes(Guid userId) => 0;

    public long GetBaseQuotaBytes(Guid userId) => 0;

public Task<IReadOnlyDictionary<Guid, QuotaInfo>> GetUsageAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyDictionary<Guid, QuotaInfo>>(userIds.ToDictionary(id => id, _ => new QuotaInfo()));

    public Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken)
      => Task.FromResult(new QuotaInfo { UsedBytes = 7, ReservedBytes = _reservedBytes, QuotaBytes = 100 });

    public Task<bool> CanRequestAsync(Guid userId, string mediaType, int episodes, CancellationToken cancellationToken) => Task.FromResult(true);

    public long ReservationBytes(RequestRecord request) => 0;

    public Task<long> GetCommittedBytesAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(0L);

    public Task<bool> IsWithinQuotaAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
  }
}
