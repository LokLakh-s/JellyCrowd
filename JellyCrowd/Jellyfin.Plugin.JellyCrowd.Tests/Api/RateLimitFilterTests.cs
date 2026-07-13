using System;
using System.Collections.Generic;
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
/// Tests for <see cref="RateLimitFilter"/> — independent read/write budgets, admin exemption.
/// </summary>
public class RateLimitFilterTests
{
  private static readonly Guid User = Guid.NewGuid();

  // Drives the filter once for the given method and returns whether the inner action ran (false = 429).
  private static async Task<bool> RunAsync(RateLimitFilter filter, string method)
  {
    var http = new DefaultHttpContext();
    http.Request.Method = method;
    var ctx = new ActionExecutingContext(
      new ActionContext(http, new RouteData(), new ActionDescriptor()),
      new List<IFilterMetadata>(),
      new Dictionary<string, object?>(),
      controller: null!);

    var ran = false;
    await filter.OnActionExecutionAsync(ctx, () =>
    {
      ran = true;
      return Task.FromResult(new ActionExecutedContext(ctx, new List<IFilterMetadata>(), controller: null!));
    });

    var throttled = ctx.Result is StatusCodeResult { StatusCode: StatusCodes.Status429TooManyRequests };
    return ran && !throttled;
  }

  private static RateLimitFilter Create(PluginConfiguration config, bool isAdmin = false)
    => new(new RateLimiter(), new FakeAccessor(isAdmin), () => config);

  [Fact]
  public async Task Get_And_Write_HaveIndependentBudgets()
  {
    var config = new PluginConfiguration { RateLimitPerMinute = 2, RateLimitGetPerMinute = 3 };
    var filter = Create(config);

    // 3 GETs are allowed, the 4th is throttled...
    Assert.True(await RunAsync(filter, "GET"));
    Assert.True(await RunAsync(filter, "GET"));
    Assert.True(await RunAsync(filter, "GET"));
    Assert.False(await RunAsync(filter, "GET"));

    // ...and the write budget is untouched by all those reads (separate bucket).
    Assert.True(await RunAsync(filter, "POST"));
    Assert.True(await RunAsync(filter, "POST"));
    Assert.False(await RunAsync(filter, "POST"));
  }

  [Fact]
  public async Task Get_IsLimited_ClosingTheTmdbAmplificationHole()
  {
    var filter = Create(new PluginConfiguration { RateLimitGetPerMinute = 1 });
    Assert.True(await RunAsync(filter, "GET"));
    Assert.False(await RunAsync(filter, "GET")); // before the fix, GETs were never limited
  }

  [Fact]
  public async Task Zero_DisablesThatBudget()
  {
    var filter = Create(new PluginConfiguration { RateLimitGetPerMinute = 0, RateLimitPerMinute = 1 });
    for (var i = 0; i < 50; i++)
    {
      Assert.True(await RunAsync(filter, "GET"));
    }
  }

  [Fact]
  public async Task Admin_IsExempt()
  {
    var filter = Create(new PluginConfiguration { RateLimitGetPerMinute = 1, RateLimitPerMinute = 1 }, isAdmin: true);
    for (var i = 0; i < 20; i++)
    {
      Assert.True(await RunAsync(filter, "GET"));
      Assert.True(await RunAsync(filter, "POST"));
    }
  }

  [Fact]
  public async Task Head_And_Options_AreNeverLimited()
  {
    var filter = Create(new PluginConfiguration { RateLimitGetPerMinute = 1, RateLimitPerMinute = 1 });
    for (var i = 0; i < 10; i++)
    {
      Assert.True(await RunAsync(filter, "HEAD"));
      Assert.True(await RunAsync(filter, "OPTIONS"));
    }
  }

  private sealed class FakeAccessor : ICurrentUserAccessor
  {
    private readonly bool _isAdmin;

    public FakeAccessor(bool isAdmin) => _isAdmin = isAdmin;

    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(_isAdmin);
  }
}
