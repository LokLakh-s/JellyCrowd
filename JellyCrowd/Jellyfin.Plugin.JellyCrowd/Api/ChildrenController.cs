using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// What a parent sees of their children: each child's wishlist (what to request for them), requests and quota.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/Children")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class ChildrenController : ControllerBase
{
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly IWatchlistStore _watchlist;
  private readonly IRequestStore _requests;
  private readonly IQuotaService _quota;
  private readonly Func<Guid, string> _resolveUserName;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="ChildrenController"/> class.
  /// </summary>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="watchlist">The watchlist store (a child's wishlist).</param>
  /// <param name="requests">The request store.</param>
  /// <param name="quota">The quota service.</param>
  /// <param name="resolveUserName">Resolves a user id to a display name.</param>
  /// <param name="config">The plugin configuration accessor (child accounts).</param>
  public ChildrenController(
    ICurrentUserAccessor userAccessor,
    IWatchlistStore watchlist,
    IRequestStore requests,
    IQuotaService quota,
    Func<Guid, string> resolveUserName,
    Func<PluginConfiguration> config)
  {
    _userAccessor = userAccessor;
    _watchlist = watchlist;
    _requests = requests;
    _quota = quota;
    _resolveUserName = resolveUserName;
    _config = config;
  }

  /// <summary>
  /// Gets the caller's children; empty when the caller is nobody's parent.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The children.</response>
  /// <returns>Each child with their wishlist, requests and quota.</returns>
  [HttpGet("Mine")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<ChildOverviewDto>>> Mine(CancellationToken cancellationToken)
  {
    var parentId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var result = new List<ChildOverviewDto>();
    foreach (var child in ChildAccountPolicy.ChildrenOf(_config(), parentId))
    {
      var wishlist = await _watchlist.GetByUserAsync(child.UserId, cancellationToken).ConfigureAwait(false);
      var requests = await _requests.GetByUserAsync(child.UserId, cancellationToken).ConfigureAwait(false);
      result.Add(new ChildOverviewDto
      {
        UserId = child.UserId,
        Name = _resolveUserName(child.UserId),
        MaxAge = child.MaxAge,
        Wishlist = wishlist.ToList(),
        Requests = requests.OrderByDescending(r => r.RequestedAt).ToList(),
        Quota = await _quota.GetUsageAsync(child.UserId, cancellationToken).ConfigureAwait(false)
      });
    }

    return Ok(result);
  }
}
