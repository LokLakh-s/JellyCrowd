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
/// Admin-only statistics endpoints (the data behind the dashboard's Stats screen).
/// </summary>
[ApiController]
[Route("JellyCrowd/Stats")]
[Authorize(Policy = "RequiresElevation")]
[Produces(MediaTypeNames.Application.Json)]
public class StatsController : ControllerBase
{
  private readonly IStatsService _stats;

  /// <summary>
  /// Initializes a new instance of the <see cref="StatsController"/> class.
  /// </summary>
  /// <param name="stats">The statistics service.</param>
  public StatsController(IStatsService stats)
  {
    _stats = stats;
  }

  /// <summary>
  /// Gets the statistics overview (playback totals, top media/users, recent activity, library counts).
  /// </summary>
  /// <param name="windowDays">The rolling window in days (0 = all time). Defaults to 30.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The overview.</response>
  /// <returns>The statistics overview.</returns>
  [HttpGet("Overview")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<StatsOverviewDto>> Overview([FromQuery] int? windowDays, CancellationToken cancellationToken)
  {
    var window = windowDays ?? 30;
    if (window < 0)
    {
      window = 0;
    }

    return Ok(await _stats.GetOverviewAsync(window, cancellationToken).ConfigureAwait(false));
  }
}
