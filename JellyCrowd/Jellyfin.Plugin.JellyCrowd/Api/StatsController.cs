using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Statistics endpoints: an admin-only server overview and a personal per-user dashboard.
/// </summary>
[ApiController]
[Route("JellyCrowd/Stats")]
[Authorize]
[Produces(MediaTypeNames.Application.Json)]
public class StatsController : ControllerBase
{
  private readonly IStatsService _stats;
  private readonly ICurrentUserAccessor _userAccessor;

  /// <summary>
  /// Initializes a new instance of the <see cref="StatsController"/> class.
  /// </summary>
  /// <param name="stats">The statistics service.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  public StatsController(IStatsService stats, ICurrentUserAccessor userAccessor)
  {
    _stats = stats;
    _userAccessor = userAccessor;
  }

  /// <summary>
  /// Gets the server statistics overview (playback totals, top media/users, recent activity, library
  /// counts). Administrators only.
  /// </summary>
  /// <param name="windowDays">The rolling window in days (0 = all time). Defaults to 30.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The overview.</response>
  /// <returns>The statistics overview.</returns>
  [HttpGet("Overview")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<StatsOverviewDto>> Overview([FromQuery] int? windowDays, CancellationToken cancellationToken)
  {
    return Ok(await _stats.GetOverviewAsync(Clamp(windowDays), cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Gets the current user's personal dashboard: their viewing statistics plus their request and quota
  /// activity. Any authenticated user.
  /// </summary>
  /// <param name="windowDays">The rolling window in days (0 = all time). Defaults to 30.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The user's dashboard.</response>
  /// <returns>The personal dashboard for the current user.</returns>
  [HttpGet("Me")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<UserDashboardDto>> Me([FromQuery] int? windowDays, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    return Ok(await _stats.GetUserDashboardAsync(userId, Clamp(windowDays), cancellationToken).ConfigureAwait(false));
  }

  private static int Clamp(int? windowDays)
  {
    var window = windowDays ?? 30;
    return window < 0 ? 0 : window;
  }
}
