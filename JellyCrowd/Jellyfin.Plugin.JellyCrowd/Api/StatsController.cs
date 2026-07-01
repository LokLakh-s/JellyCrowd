using System;
using System.Collections.Generic;
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
  private readonly IPlaybackReportingImporter _importer;

  /// <summary>
  /// Initializes a new instance of the <see cref="StatsController"/> class.
  /// </summary>
  /// <param name="stats">The statistics service.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="importer">The Playback Reporting importer.</param>
  public StatsController(IStatsService stats, ICurrentUserAccessor userAccessor, IPlaybackReportingImporter importer)
  {
    _stats = stats;
    _userAccessor = userAccessor;
    _importer = importer;
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
  /// Gets a specific user's dashboard (their viewing, request and quota activity). Administrators only —
  /// the per-user drill-down on the admin Stats screen.
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="windowDays">The rolling window in days (0 = all time). Defaults to 30.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The user's dashboard.</response>
  /// <returns>The requested user's dashboard.</returns>
  [HttpGet("User/{userId}")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<UserDashboardDto>> UserDashboard(Guid userId, [FromQuery] int? windowDays, CancellationToken cancellationToken)
  {
    return Ok(await _stats.GetUserDashboardAsync(userId, Clamp(windowDays), cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Imports historical playback from the Playback Reporting plugin's database (when installed) into the
  /// statistics history. Administrators only; safe to run repeatedly — already-known plays are skipped.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The import outcome.</response>
  /// <returns>The number of records found and imported.</returns>
  [HttpPost("ImportPlaybackReporting")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<ImportResultDto>> ImportPlaybackReporting(CancellationToken cancellationToken)
  {
    return Ok(await _importer.ImportAsync(cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Gets the live "now playing" sessions. Administrators only.
  /// </summary>
  /// <response code="200">The live sessions.</response>
  /// <returns>The currently-playing sessions.</returns>
  [HttpGet("Sessions")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<IReadOnlyList<StatsSessionDto>> Sessions()
  {
    return Ok(_stats.GetSessions());
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
