using System;
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
/// The current user's opt-in to automatic requests (the next season of a show they are watching).
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/AutoRequests")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class AutoRequestsController : ControllerBase
{
  private readonly IUserPrefsStore _prefs;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="AutoRequestsController"/> class.
  /// </summary>
  /// <param name="prefs">The user preferences store (holds the opt-in).</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="config">The plugin configuration accessor.</param>
  public AutoRequestsController(IUserPrefsStore prefs, ICurrentUserAccessor userAccessor, Func<PluginConfiguration> config)
  {
    _prefs = prefs;
    _userAccessor = userAccessor;
    _config = config;
  }

  /// <summary>
  /// Gets whether the automatic next-season request is offered and whether the caller opted in.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The caller's setting.</response>
  /// <returns>The setting.</returns>
  [HttpGet("Mine")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<AutoNextSeasonDto>> Mine(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var prefs = await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false);
    var config = _config();
    return Ok(new AutoNextSeasonDto
    {
      Available = config.AutoNextSeasonEnabled,
      Enabled = prefs.AutoRequestNextSeason,
      EpisodesLeft = Math.Clamp(config.AutoNextSeasonEpisodesLeft, 0, NextSeasonPlanner.MaxEpisodesLeft)
    });
  }

  /// <summary>
  /// Turns the caller's automatic next-season request on or off.
  /// </summary>
  /// <param name="enabled">Whether to request the next season automatically.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The preference was saved.</response>
  /// <response code="403">The administrator does not offer the feature (turning it off is always allowed).</response>
  /// <returns>No content.</returns>
  [HttpPost("Mine")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status403Forbidden)]
  public async Task<IActionResult> SetMine([FromQuery] bool enabled, CancellationToken cancellationToken)
  {
    if (enabled && !_config().AutoNextSeasonEnabled)
    {
      return StatusCode(StatusCodes.Status403Forbidden, "Automatic requests are not offered on this server.");
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);

    // Read-modify-write so the notification preferences on the same record are preserved.
    var prefs = await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false);
    prefs.UserId = userId;
    prefs.AutoRequestNextSeason = enabled;
    await _prefs.SetAsync(prefs, cancellationToken).ConfigureAwait(false);
    return NoContent();
  }
}
