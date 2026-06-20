using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Per-user rate limit on mutating (POST/PUT/DELETE/PATCH) requests to the plugin's API, on top of the
/// per-period request cap. Administrators are exempt. Returns 429 when the limit is exceeded.
/// </summary>
public sealed class RateLimitFilter : IAsyncActionFilter
{
  private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

  private readonly RateLimiter _limiter;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="RateLimitFilter"/> class.
  /// </summary>
  /// <param name="limiter">The shared rate limiter.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  public RateLimitFilter(RateLimiter limiter, ICurrentUserAccessor userAccessor, Func<PluginConfiguration> config)
  {
    _limiter = limiter;
    _userAccessor = userAccessor;
    _config = config;
  }

  /// <inheritdoc />
  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(next);

    var request = context.HttpContext.Request;
    var max = _config().RateLimitPerMinute;
    var mutating = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method);

    if (max <= 0 || !mutating || await _userAccessor.IsAdministratorAsync(request).ConfigureAwait(false))
    {
      await next().ConfigureAwait(false);
      return;
    }

    var userId = await _userAccessor.GetUserIdAsync(request).ConfigureAwait(false);
    var key = userId == Guid.Empty
      ? (context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous")
      : userId.ToString("N");

    if (!_limiter.TryAcquire(key, max, Window, DateTime.UtcNow))
    {
      context.Result = new StatusCodeResult(StatusCodes.Status429TooManyRequests);
      return;
    }

    await next().ConfigureAwait(false);
  }
}
