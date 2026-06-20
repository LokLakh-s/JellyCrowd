using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="PluginVisibilityFilter"/> ("config mode" access gate).
/// </summary>
public class PluginVisibilityFilterTests
{
  private static ActionExecutingContext Context()
  {
    var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
    return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());
  }

  private static async Task<bool> RunAsync(PluginConfiguration config, bool admin, ActionExecutingContext context)
  {
    var filter = new PluginVisibilityFilter(() => config, new FakeAccessor(admin));
    var proceeded = false;
    await filter.OnActionExecutionAsync(context, () =>
    {
      proceeded = true;
      return Task.FromResult<ActionExecutedContext>(null!);
    });
    return proceeded;
  }

  [Fact]
  public async Task NotHidden_Proceeds()
  {
    var ctx = Context();
    var proceeded = await RunAsync(new PluginConfiguration { HiddenFromUsers = false }, admin: false, ctx);

    Assert.True(proceeded);
    Assert.Null(ctx.Result);
  }

  [Fact]
  public async Task HiddenNonAdmin_Returns403()
  {
    var ctx = Context();
    var proceeded = await RunAsync(new PluginConfiguration { HiddenFromUsers = true }, admin: false, ctx);

    Assert.False(proceeded);
    Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(ctx.Result).StatusCode);
  }

  [Fact]
  public async Task HiddenAdmin_Proceeds()
  {
    var ctx = Context();
    var proceeded = await RunAsync(new PluginConfiguration { HiddenFromUsers = true }, admin: true, ctx);

    Assert.True(proceeded);
    Assert.Null(ctx.Result);
  }

  private sealed class FakeAccessor : ICurrentUserAccessor
  {
    private readonly bool _admin;

    public FakeAccessor(bool admin) => _admin = admin;

    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(Guid.NewGuid());

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(_admin);
  }
}
