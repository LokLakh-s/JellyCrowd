using System;
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
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
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

  private QuotaController CreateController()
    => new(new FakeQuotaService(), new FakeUserAccessor(), _store, new FakeMatcher())
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

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

  private sealed class FakeMatcher : ILibraryMatcher
  {
    public bool Exists(string mediaType, int tmdbId) => true;

    public string? FindItemId(string mediaType, int tmdbId) => null;

    public long GetSizeBytes(string mediaType, int tmdbId) => 123456;
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);
  }

  private sealed class FakeQuotaService : IQuotaService
  {
    public long GetQuotaBytes(Guid userId) => 0;

    public Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(new QuotaInfo());

    public Task<bool> CanRequestAsync(Guid userId, string mediaType, CancellationToken cancellationToken) => Task.FromResult(true);
  }
}
