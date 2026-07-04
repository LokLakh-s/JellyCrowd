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
/// Read-only access to the plugin's internal activity log (admin Logs tab).
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("JellyCrowd/Logs")]
[Produces(MediaTypeNames.Application.Json)]
public class LogsController : ControllerBase
{
  private const int MaxLimit = 500;

  private readonly IActivityLog _activityLog;

  /// <summary>
  /// Initializes a new instance of the <see cref="LogsController"/> class.
  /// </summary>
  /// <param name="activityLog">The activity log.</param>
  public LogsController(IActivityLog activityLog) => _activityLog = activityLog;

  /// <summary>
  /// Queries the activity log, newest first.
  /// </summary>
  /// <param name="term">Free-text filter on the message, or <c>null</c>.</param>
  /// <param name="category">Category filter (<c>request</c>, <c>download</c>, …), or <c>null</c>.</param>
  /// <param name="level">Level filter (<c>info</c>, <c>warning</c>, <c>error</c>), or <c>null</c>.</param>
  /// <param name="user">User filter (exact display name), or <c>null</c>.</param>
  /// <param name="limit">Maximum number of entries (capped at 500).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The matching log entries.</response>
  /// <returns>The activity entries that match the filters.</returns>
  [HttpGet]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<ActivityEntry>>> Get(
    [FromQuery] string? term,
    [FromQuery] string? category,
    [FromQuery] string? level,
    [FromQuery] string? user,
    [FromQuery] int limit,
    CancellationToken cancellationToken)
  {
    var capped = limit <= 0 ? 200 : (limit > MaxLimit ? MaxLimit : limit);
    var entries = await _activityLog.QueryAsync(term, category, level, user, capped, cancellationToken).ConfigureAwait(false);
    return Ok(entries);
  }
}
