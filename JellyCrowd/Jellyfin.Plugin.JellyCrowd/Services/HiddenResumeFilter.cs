using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Takes what a user removed out of Jellyfin's own "Continue watching" and "Next up" answers — on the
/// server, so every client (web, Android TV, Swiftfin, Findroid…) shows the same rows. Registered globally
/// on MVC but inert everywhere else: it only acts on the resume and next-up actions, widens their limit by
/// the number of removals so a row stays full, then drops the removed items from the result. Any failure
/// leaves Jellyfin's answer untouched.
/// </summary>
public sealed class HiddenResumeFilter : IAsyncActionFilter
{
  private readonly IHiddenResumeStore _store;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly ILogger<HiddenResumeFilter> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="HiddenResumeFilter"/> class.
  /// </summary>
  /// <param name="store">The removals store.</param>
  /// <param name="userAccessor">Resolves the calling user.</param>
  /// <param name="logger">The logger.</param>
  public HiddenResumeFilter(IHiddenResumeStore store, ICurrentUserAccessor userAccessor, ILogger<HiddenResumeFilter> logger)
  {
    _store = store;
    _userAccessor = userAccessor;
    _logger = logger;
  }

  /// <summary>
  /// Whether an action is one of Jellyfin's resume or next-up lists (same names on 10.11 and 12).
  /// </summary>
  /// <param name="controllerName">The controller name (without the "Controller" suffix).</param>
  /// <param name="actionName">The action (method) name.</param>
  /// <returns><c>true</c> for the lists this filter acts on.</returns>
  public static bool IsResumeOrNextUp(string? controllerName, string? actionName)
    => (string.Equals(controllerName, "Items", StringComparison.Ordinal)
        && (string.Equals(actionName, "GetResumeItems", StringComparison.Ordinal)
            || string.Equals(actionName, "GetResumeItemsLegacy", StringComparison.Ordinal)))
      || (string.Equals(controllerName, "TvShows", StringComparison.Ordinal)
        && string.Equals(actionName, "GetNextUp", StringComparison.Ordinal));

  /// <inheritdoc />
  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(next);
    if (context.ActionDescriptor is not ControllerActionDescriptor action
        || !IsResumeOrNextUp(action.ControllerName, action.ActionName))
    {
      await next().ConfigureAwait(false);
      return;
    }

    IReadOnlyList<HiddenResumeEntry> hidden = Array.Empty<HiddenResumeEntry>();
    int? requestedLimit = null;
    try
    {
      var userId = await ResolveUserIdAsync(context).ConfigureAwait(false);
      if (userId != Guid.Empty)
      {
        hidden = await _store.GetByUserAsync(userId, context.HttpContext.RequestAborted).ConfigureAwait(false);
      }

      // Widen a first page only: past it, the removed items' positions are unknown and a short page is
      // the lesser evil.
      if (hidden.Count > 0
          && context.ActionArguments.TryGetValue("limit", out var limitArg) && limitArg is int limit
          && (!context.ActionArguments.TryGetValue("startIndex", out var startArg) || startArg is null || (startArg is int start && start <= 0)))
      {
        requestedLimit = limit;
        context.ActionArguments["limit"] = limit + HiddenResumePolicy.ExtraLimit(hidden.Count);
      }
    }
#pragma warning disable CA1031 // Never break Jellyfin's own endpoint over a removal list.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd: could not read the Continue watching removals; serving the list as is.");
      hidden = Array.Empty<HiddenResumeEntry>();
    }

    var executed = await next().ConfigureAwait(false);
    if (hidden.Count == 0 || executed.Result is not ObjectResult { Value: QueryResult<BaseItemDto> result })
    {
      return;
    }

    try
    {
      HiddenResumePolicy.Apply(result, hidden, requestedLimit);
    }
#pragma warning disable CA1031 // Same: the unfiltered answer beats a broken one.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd: could not filter the Continue watching removals.");
    }
  }

  // The user the list is for: an explicit userId argument (an administrator may ask for someone else's
  // list, and the legacy route carries it in the path), otherwise the caller.
  private async Task<Guid> ResolveUserIdAsync(ActionExecutingContext context)
  {
    if (context.ActionArguments.TryGetValue("userId", out var arg) && arg is Guid explicitId && explicitId != Guid.Empty)
    {
      return explicitId;
    }

    return await _userAccessor.GetUserIdAsync(context.HttpContext.Request).ConfigureAwait(false);
  }
}
