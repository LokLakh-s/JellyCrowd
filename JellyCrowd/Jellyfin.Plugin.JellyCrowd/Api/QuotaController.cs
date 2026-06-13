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
/// Exposes the current user's disk-quota usage. Per-user quota overrides are managed by admins
/// through the plugin configuration page.
/// </summary>
[ApiController]
[Route("JellyCrowd/Quota")]
[Produces(MediaTypeNames.Application.Json)]
public class QuotaController : ControllerBase
{
  private readonly IQuotaService _quotaService;
  private readonly ICurrentUserAccessor _userAccessor;

  /// <summary>
  /// Initializes a new instance of the <see cref="QuotaController"/> class.
  /// </summary>
  /// <param name="quotaService">The quota service.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  public QuotaController(IQuotaService quotaService, ICurrentUserAccessor userAccessor)
  {
    _quotaService = quotaService;
    _userAccessor = userAccessor;
  }

  /// <summary>
  /// Gets the current user's quota usage.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The usage snapshot.</response>
  /// <returns>The caller's used/quota bytes.</returns>
  [HttpGet("Me")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<QuotaInfo>> Me(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var info = await _quotaService.GetUsageAsync(userId, cancellationToken).ConfigureAwait(false);
    return Ok(info);
  }
}
