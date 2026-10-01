using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="ChildrenController"/>.
/// </summary>
public class ChildrenControllerTests
{
  private static readonly Guid Parent = Guid.NewGuid();
  private static readonly Guid Kid = Guid.NewGuid();

  private static ChildrenController Create(Guid caller)
  {
    var config = new PluginConfiguration();
    var kid = new ChildAccount { UserId = Kid, MaxAge = 10 };
    kid.ParentIds.Add(Parent);
    config.ChildAccounts.Add(kid);

    var watchlist = Mock.Of<IWatchlistStore>(w => w.GetByUserAsync(Kid, It.IsAny<CancellationToken>())
      == Task.FromResult<IReadOnlyList<WatchlistEntry>>(new List<WatchlistEntry> { new() { UserId = Kid, TmdbId = 862, MediaType = "movie", Title = "Toy Story" } }));
    var requests = Mock.Of<IRequestStore>(r => r.GetByUserAsync(Kid, It.IsAny<CancellationToken>())
      == Task.FromResult<IReadOnlyList<RequestRecord>>(new List<RequestRecord> { new() { UserId = Kid, TmdbId = 12, MediaType = "movie", Title = "Finding Nemo" } }));
    var quota = new Mock<IQuotaService>();
    quota.Setup(q => q.GetUsageAsync(Kid, It.IsAny<CancellationToken>())).ReturnsAsync(new QuotaInfo { QuotaBytes = 100 });
    var accessor = Mock.Of<ICurrentUserAccessor>(a => a.GetUserIdAsync(It.IsAny<HttpRequest>()) == Task.FromResult(caller));
    return new ChildrenController(accessor, watchlist, requests, quota.Object, id => id == Kid ? "Lou" : "?", () => config)
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
  }

  [Fact]
  public async Task Mine_AParentSeesTheirChildsWishlistRequestsAndQuota()
  {
    var result = await Create(Parent).Mine(CancellationToken.None);

    var child = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ChildOverviewDto>>(Assert.IsType<OkObjectResult>(result.Result).Value));
    Assert.Equal(Kid, child.UserId);
    Assert.Equal("Lou", child.Name);
    Assert.Equal(10, child.MaxAge);
    Assert.Equal("Toy Story", Assert.Single(child.Wishlist).Title);
    Assert.Equal("Finding Nemo", Assert.Single(child.Requests).Title);
    Assert.Equal(100, child.Quota!.QuotaBytes);
  }

  [Fact]
  public async Task Mine_SomeoneWhoIsNoParent_SeesNothing()
  {
    var result = await Create(Guid.NewGuid()).Mine(CancellationToken.None);

    Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ChildOverviewDto>>(Assert.IsType<OkObjectResult>(result.Result).Value));
  }
}
