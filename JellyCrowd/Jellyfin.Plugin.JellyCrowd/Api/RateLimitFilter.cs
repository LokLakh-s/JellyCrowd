using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Per-user rate limit on the plugin's API, on top of the per-period request cap. Writes and reads have
/// independent per-minute budgets (a small write cap; a generous read cap that only stops scripted loops
/// hammering the TMDB-backed catalog GETs). Administrators are exempt. Returns 429 when a limit is hit.
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
    var config = _config();
    var isGet = HttpMethods.IsGet(request.Method);
    var isMutating = !isGet && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method);

    // Reads and writes get separate budgets (and separate buckets) so a heavy read backend — the catalog
    // GETs fan out to TMDB — is capped without spending, or being blocked by, the write budget. HEAD and
    // OPTIONS are never limited.
    var max = isMutating ? config.RateLimitPerMinute : (isGet ? config.RateLimitGetPerMinute : 0);
    if (max <= 0 || await _userAccessor.IsAdministratorAsync(request).ConfigureAwait(false))
    {
      await next().ConfigureAwait(false);
      return;
    }

    var userId = await _userAccessor.GetUserIdAsync(request).ConfigureAwait(false);
    var identity = userId == Guid.Empty
      ? (context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous")
      : userId.ToString("N");
    var key = isGet ? identity + ":get" : identity;

    if (!_limiter.TryAcquire(key, max, Window, DateTime.UtcNow))
    {
      context.Result = new StatusCodeResult(StatusCodes.Status429TooManyRequests);
      return;
    }

    await next().ConfigureAwait(false);
  }
}
