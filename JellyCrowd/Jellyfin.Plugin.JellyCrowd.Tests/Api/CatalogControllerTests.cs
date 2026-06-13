using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="CatalogController"/>.
/// </summary>
public class CatalogControllerTests
{
  private static CatalogController CreateController(ITmdbClient client)
    => new(client, new FakeLibraryMatcher(), NullLogger<CatalogController>.Instance);

  private sealed class FakeLibraryMatcher : ILibraryMatcher
  {
    public bool Result { get; init; }

    public bool Exists(string mediaType, int tmdbId) => Result;

    public string? FindItemId(string mediaType, int tmdbId) => Result ? "abc123" : null;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;
  }

  [Fact]
  public async Task GetTrending_ReturnsOkWithItems()
  {
    var items = new List<CatalogItem> { new() { TmdbId = 1, MediaType = "movie", Title = "A" } };
    var controller = CreateController(new FakeTmdbClient { Results = items });

    var result = await controller.GetTrending(null, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var payload = Assert.IsAssignableFrom<IReadOnlyList<CatalogItem>>(ok.Value);
    Assert.Single(payload);
  }

  [Fact]
  public async Task Episodes_ReturnsOk()
  {
    var controller = CreateController(new FakeTmdbClient());

    var result = await controller.Episodes(7, 2, null, CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
  }

  [Fact]
  public async Task Calendar_ReturnsUpcomingOrderedByDate()
  {
    var items = new List<CatalogItem>
    {
      new() { TmdbId = 1, MediaType = "movie", Title = "Later", ReleaseDate = "2030-06-01" },
      new() { TmdbId = 2, MediaType = "tv", Title = "Soon", ReleaseDate = "2030-01-01" },
      new() { TmdbId = 3, MediaType = "movie", Title = "Past", ReleaseDate = "1999-01-01" }
    };
    var controller = CreateController(new FakeTmdbClient { Results = items });

    var result = await controller.Calendar(null, null, null, null, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var payload = Assert.IsAssignableFrom<IReadOnlyList<CatalogItem>>(ok.Value);
    // Past dropped; each media type fetch returns the same list, so both upcoming appear twice.
    Assert.All(payload, item => Assert.NotEqual(3, item.TmdbId));
    Assert.Equal("Soon", payload[0].Title);
  }

  [Fact]
  public async Task Calendar_WithRange_ReturnsDatedDedupedOrdered()
  {
    var items = new List<CatalogItem>
    {
      new() { TmdbId = 10, MediaType = "movie", Title = "Mid", ReleaseDate = "2026-06-15" },
      new() { TmdbId = 11, MediaType = "movie", Title = "Early", ReleaseDate = "2026-06-02" },
      new() { TmdbId = 12, MediaType = "movie", Title = "Undated", ReleaseDate = null }
    };
    var controller = CreateController(new FakeTmdbClient { Results = items });

    var result = await controller.Calendar(null, null, "2026-06-01", "2026-06-30", CancellationToken.None);

    var payload = Assert.IsAssignableFrom<IReadOnlyList<CatalogItem>>(Assert.IsType<OkObjectResult>(result.Result).Value);
    // Undated dropped, deduped across movie+tv fetches, ordered ascending.
    Assert.Equal(new[] { "Early", "Mid" }, payload.Select(i => i.Title).ToArray());
  }

  [Fact]
  public async Task Search_WithEmptyQuery_ReturnsBadRequest()
  {
    var controller = CreateController(new FakeTmdbClient());

    var result = await controller.Search("   ", null, null, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task Search_WithQuery_ReturnsOk()
  {
    var controller = CreateController(new FakeTmdbClient { Results = new List<CatalogItem>() });

    var result = await controller.Search("matrix", "fr-FR", null, CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
  }

  [Fact]
  public async Task GetDetails_WithInvalidMediaType_ReturnsBadRequest()
  {
    var controller = CreateController(new FakeTmdbClient());

    var result = await controller.GetDetails("book", 1, null, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task Discover_ReturnsOkWithItems()
  {
    var items = new List<CatalogItem> { new() { TmdbId = 7, MediaType = "movie", Title = "D" } };
    var controller = CreateController(new FakeTmdbClient { Results = items });

    var result = await controller.Discover("movie", "28", 2000, 2020, 6.0, 9.0, "rating", null, null, null, null, CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
  }

  [Fact]
  public async Task Genres_InvalidMediaType_ReturnsBadRequest()
  {
    var controller = CreateController(new FakeTmdbClient());

    var result = await controller.Genres("book", null, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task GetDetails_WhenNotFound_Returns404()
  {
    var controller = CreateController(new FakeTmdbClient { Detail = null });

    var result = await controller.GetDetails("movie", 99, null, CancellationToken.None);

    Assert.IsType<NotFoundResult>(result.Result);
  }

  [Fact]
  public async Task GetTrending_WhenNotConfigured_Returns503()
  {
    var controller = CreateController(new FakeTmdbClient { Throw = new InvalidOperationException("no key") });

    var result = await controller.GetTrending(null, CancellationToken.None);

    var obj = Assert.IsType<ObjectResult>(result.Result);
    Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
  }

  private sealed class FakeTmdbClient : ITmdbClient
  {
    public IReadOnlyList<CatalogItem> Results { get; set; } = new List<CatalogItem>();

    public CatalogItem? Detail { get; set; } = new() { TmdbId = 1, MediaType = "movie", Title = "Detail" };

    public Exception? Throw { get; set; }

    public Task<IReadOnlyList<CatalogItem>> GetTrendingAsync(string language, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult(Results);
    }

    public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, string language, int page, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult(Results);
    }

    public Task<CatalogItem?> GetDetailsAsync(string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult(Detail);
    }

    public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string mediaType, DiscoverQuery query, string language, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult(Results);
    }

    public Task<IReadOnlyList<Genre>> GetGenresAsync(string mediaType, string language, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult<IReadOnlyList<Genre>>(new List<Genre>());
    }

    public Task<IReadOnlyList<Season>> GetSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult<IReadOnlyList<Season>>(new List<Season>());
    }

    public Task<IReadOnlyList<Episode>> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, string language, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult<IReadOnlyList<Episode>>(new List<Episode>());
    }

    public Task<IReadOnlyList<WatchProvider>> GetWatchProvidersAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult<IReadOnlyList<WatchProvider>>(new List<WatchProvider>());
    }

    public Task<int?> GetTvdbIdAsync(int tmdbId, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult<int?>(null);
    }

    public Task<IReadOnlyList<CatalogItem>> GetUpcomingAsync(string mediaType, string region, string language, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      return Task.FromResult(Results);
    }

    public Task<IReadOnlyList<CatalogItem>> GetReleasesAsync(string mediaType, string fromDate, string toDate, string region, string language, int page, CancellationToken cancellationToken)
    {
      if (Throw is not null)
      {
        throw Throw;
      }

      // Page 1 returns the seeded results; later pages are empty (stops the controller's paging loop).
      return Task.FromResult(page <= 1 ? Results : (IReadOnlyList<CatalogItem>)new List<CatalogItem>());
    }
  }
}
