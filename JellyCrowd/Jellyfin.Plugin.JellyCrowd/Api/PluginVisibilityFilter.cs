using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
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
  // Jellyfin grants this role to administrators (and API keys); it is what the "RequiresElevation"
  // policy checks under the hood.
  private const string AdministratorRole = "Administrator";

  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="PluginVisibilityFilter"/> class.
  /// </summary>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  public PluginVisibilityFilter(Func<PluginConfiguration> config) => _config = config;

  /// <inheritdoc />
  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(next);

    if (_config().HiddenFromUsers && !(context.HttpContext.User?.IsInRole(AdministratorRole) ?? false))
    {
      context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
      return;
    }

    await next().ConfigureAwait(false);
  }
}
