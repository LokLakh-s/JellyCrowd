using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Enforces "config mode" (<see cref="PluginConfiguration.HiddenFromUsers"/>) on user-facing
/// controllers: when the plugin is hidden, only administrators may call the endpoint; everyone else
/// gets a 403. Lets the admin keep the plugin hidden until it is configured and working.
/// </summary>
public sealed class PluginVisibilityFilter : IAsyncActionFilter
{
  private readonly Func<PluginConfiguration> _config;
  private readonly ICurrentUserAccessor _userAccessor;

  /// <summary>
  /// Initializes a new instance of the <see cref="PluginVisibilityFilter"/> class.
  /// </summary>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="userAccessor">The current-user accessor (to resolve administrator status).</param>
  public PluginVisibilityFilter(Func<PluginConfiguration> config, ICurrentUserAccessor userAccessor)
  {
    _config = config;
    _userAccessor = userAccessor;
  }

  /// <inheritdoc />
  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(next);

    if (_config().HiddenFromUsers && !await _userAccessor.IsAdministratorAsync(context.HttpContext.Request).ConfigureAwait(false))
    {
      context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
      return;
    }

    await next().ConfigureAwait(false);
  }
}
