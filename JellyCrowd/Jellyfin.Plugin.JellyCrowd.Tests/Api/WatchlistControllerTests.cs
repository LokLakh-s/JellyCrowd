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
/// Tests for <see cref="WatchlistController"/>.
/// </summary>
public sealed class WatchlistControllerTests : IDisposable
{
  private static readonly Guid User = Guid.NewGuid();

  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonWatchlistStore _store;

  public WatchlistControllerTests()
  {
    _store = new JsonWatchlistStore(_path);
  }

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private WatchlistController CreateController()
    => new(_store, new FakeUserAccessor(User))
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

  [Fact]
  public async Task Add_Then_Mine_ReturnsEntryForUser()
  {
    await CreateController().Add(new WatchlistItemDto { TmdbId = 5, MediaType = "movie", Title = "Dune" }, CancellationToken.None);

    var result = await CreateController().Mine(CancellationToken.None);

    var items = Assert.IsAssignableFrom<IReadOnlyList<WatchlistEntry>>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Single(items);
    Assert.Equal(User, items[0].UserId);
    Assert.Equal(5, items[0].TmdbId);
  }

  [Fact]
  public async Task Add_InvalidMediaType_ReturnsBadRequest()
  {
    var result = await CreateController().Add(new WatchlistItemDto { TmdbId = 5, MediaType = "book", Title = "X" }, CancellationToken.None);
    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task Remove_RemovesEntry_ReturnsNoContent()
  {
    await CreateController().Add(new WatchlistItemDto { TmdbId = 5, MediaType = "movie", Title = "Dune" }, CancellationToken.None);

    var result = await CreateController().Remove(new WatchlistItemDto { TmdbId = 5, MediaType = "movie" }, CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
    Assert.Empty(await _store.GetByUserAsync(User, CancellationToken.None));
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    private readonly Guid _userId;

    public FakeUserAccessor(Guid userId) => _userId = userId;

    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(_userId);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(false);
  }
}
