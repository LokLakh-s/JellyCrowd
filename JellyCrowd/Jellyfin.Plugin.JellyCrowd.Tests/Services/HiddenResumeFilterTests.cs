using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="HiddenResumeFilter"/> and <see cref="HiddenResumePolicy"/>.
/// </summary>
public class HiddenResumeFilterTests
{
  private static readonly Guid Viewer = Guid.NewGuid();
  private static readonly Guid Movie = Guid.NewGuid();
  private static readonly Guid Show = Guid.NewGuid();
  private static readonly Guid ShowEpisode = Guid.NewGuid();
  private static readonly Guid Other = Guid.NewGuid();

  private static List<BaseItemDto> Rows() => new()
  {
    new BaseItemDto { Id = Movie },
    new BaseItemDto { Id = ShowEpisode, SeriesId = Show },
    new BaseItemDto { Id = Other },
  };

  private static List<HiddenResumeEntry> HiddenMovieAndShow() => new()
  {
    new HiddenResumeEntry { UserId = Viewer, ItemId = Movie },
    new HiddenResumeEntry { UserId = Viewer, ItemId = Guid.NewGuid(), SeriesId = Show },
  };

  private static async Task<(object? LimitSeen, QueryResult<BaseItemDto> Result)> Run(
    string controller,
    string action,
    IReadOnlyList<HiddenResumeEntry> hidden,
    Dictionary<string, object?> args,
    Func<Guid, IReadOnlyList<HiddenResumeEntry>>? perUser = null)
  {
    var store = new Mock<IHiddenResumeStore>();
    store.Setup(s => s.GetByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((Guid id, CancellationToken _) => perUser is null ? hidden : perUser(id));
    var accessor = Mock.Of<ICurrentUserAccessor>(a => a.GetUserIdAsync(It.IsAny<HttpRequest>()) == Task.FromResult(Viewer));
    var filter = new HiddenResumeFilter(store.Object, accessor, NullLogger<HiddenResumeFilter>.Instance);

    var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action };
    var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
    var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), args, new object());
    var result = new QueryResult<BaseItemDto>(0, 3, Rows());
    object? limitSeen = null;
    await filter.OnActionExecutionAsync(executing, () =>
    {
      limitSeen = args.TryGetValue("limit", out var l) ? l : null;
      return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()) { Result = new ObjectResult(result) });
    });
    return (limitSeen, result);
  }

  [Theory]
  [InlineData("Items", "GetResumeItems", true)]
  [InlineData("Items", "GetResumeItemsLegacy", true)]
  [InlineData("TvShows", "GetNextUp", true)]
  [InlineData("Items", "GetItems", false)]
  [InlineData("TvShows", "GetEpisodes", false)]
  [InlineData(null, null, false)]
  public void IsResumeOrNextUp_OnlyTheseThreeActions(string? controller, string? action, bool expected)
  {
    Assert.Equal(expected, HiddenResumeFilter.IsResumeOrNextUp(controller, action));
  }

  [Fact]
  public async Task Resume_DropsTheRemovedMovieAndShow_AndWidensTheLimit()
  {
    var args = new Dictionary<string, object?> { ["limit"] = 1, ["startIndex"] = null, ["userId"] = null };

    var (limitSeen, result) = await Run("Items", "GetResumeItems", HiddenMovieAndShow(), args);

    Assert.Equal(3, limitSeen); // 1 asked + 2 removals
    Assert.Equal(new[] { Other }, result.Items.Select(i => i.Id));
    Assert.Equal(1, result.TotalRecordCount);
  }

  [Fact]
  public async Task NextUp_IsFilteredToo()
  {
    var args = new Dictionary<string, object?> { ["limit"] = 24 };

    var (_, result) = await Run("TvShows", "GetNextUp", HiddenMovieAndShow(), args);

    Assert.DoesNotContain(result.Items, i => i.SeriesId == Show);
  }

  [Fact]
  public async Task WidenedQuery_IsTrimmedBackToTheRequestedSize()
  {
    var hidden = new List<HiddenResumeEntry> { new() { UserId = Viewer, ItemId = Guid.NewGuid() } }; // removes nothing on this page
    var args = new Dictionary<string, object?> { ["limit"] = 2 };

    var (limitSeen, result) = await Run("Items", "GetResumeItems", hidden, args);

    Assert.Equal(3, limitSeen);
    Assert.Equal(2, result.Items.Count);
  }

  [Fact]
  public async Task LaterPage_IsNotWidened()
  {
    var args = new Dictionary<string, object?> { ["limit"] = 5, ["startIndex"] = 10 };

    var (limitSeen, _) = await Run("Items", "GetResumeItems", HiddenMovieAndShow(), args);

    Assert.Equal(5, limitSeen);
  }

  [Fact]
  public async Task NoRemovals_LeavesTheAnswerAlone()
  {
    var args = new Dictionary<string, object?> { ["limit"] = 5 };

    var (limitSeen, result) = await Run("Items", "GetResumeItems", Array.Empty<HiddenResumeEntry>(), args);

    Assert.Equal(5, limitSeen);
    Assert.Equal(3, result.Items.Count);
  }

  [Fact]
  public async Task OtherActions_AreUntouched()
  {
    var args = new Dictionary<string, object?> { ["limit"] = 5 };

    var (limitSeen, result) = await Run("Items", "GetItems", HiddenMovieAndShow(), args);

    Assert.Equal(5, limitSeen);
    Assert.Equal(3, result.Items.Count);
  }

  [Fact]
  public async Task ExplicitUserId_UsesThatUsersRemovals()
  {
    // An administrator asking for someone else's list gets that person's rows, not their own filter.
    var someoneElse = Guid.NewGuid();
    var args = new Dictionary<string, object?> { ["limit"] = 5, ["userId"] = someoneElse };

    var (_, result) = await Run("Items", "GetResumeItemsLegacy", Array.Empty<HiddenResumeEntry>(), args, id => id == someoneElse ? HiddenMovieAndShow() : Array.Empty<HiddenResumeEntry>());

    Assert.Equal(new[] { Other }, result.Items.Select(i => i.Id));
  }

  [Fact]
  public async Task StoreFailure_LeavesTheAnswerAlone()
  {
    var store = new Mock<IHiddenResumeStore>();
    store.Setup(s => s.GetByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("disk"));
    var accessor = Mock.Of<ICurrentUserAccessor>(a => a.GetUserIdAsync(It.IsAny<HttpRequest>()) == Task.FromResult(Viewer));
    var filter = new HiddenResumeFilter(store.Object, accessor, NullLogger<HiddenResumeFilter>.Instance);
    var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor { ControllerName = "Items", ActionName = "GetResumeItems" });
    var args = new Dictionary<string, object?> { ["limit"] = 5 };
    var result = new QueryResult<BaseItemDto>(0, 3, Rows());

    await filter.OnActionExecutionAsync(
      new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), args, new object()),
      () => Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()) { Result = new ObjectResult(result) }));

    Assert.Equal(5, args["limit"]);
    Assert.Equal(3, result.Items.Count);
  }

  [Fact]
  public void ExtraLimit_IsBounded()
  {
    Assert.Equal(0, HiddenResumePolicy.ExtraLimit(-1));
    Assert.Equal(7, HiddenResumePolicy.ExtraLimit(7));
    Assert.Equal(HiddenResumePolicy.MaxExtra, HiddenResumePolicy.ExtraLimit(10_000));
  }

  [Fact]
  public void IsHidden_ARemovedShowCoversItsSeriesItemToo()
  {
    var hidden = HiddenMovieAndShow();

    Assert.True(HiddenResumePolicy.IsHidden(new BaseItemDto { Id = Show }, hidden));
    Assert.False(HiddenResumePolicy.IsHidden(new BaseItemDto { Id = Other }, hidden));
  }
}
