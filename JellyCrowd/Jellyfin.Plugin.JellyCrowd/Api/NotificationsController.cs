using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Admin endpoints to test the notification channels.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("JellyCrowd/Notifications")]
[Produces(MediaTypeNames.Application.Json)]
public class NotificationsController : ControllerBase
{
  private readonly INotificationService _notificationService;

  /// <summary>
  /// Initializes a new instance of the <see cref="NotificationsController"/> class.
  /// </summary>
  /// <param name="notificationService">The notification service.</param>
  public NotificationsController(INotificationService notificationService)
  {
    _notificationService = notificationService;
  }

  /// <summary>
  /// Sends a test notification to a channel (<c>discord</c> or <c>email</c>).
  /// </summary>
  /// <param name="channel">The channel to test.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The test notification was sent.</response>
  /// <response code="400">The channel failed or is not configured (details in the response).</response>
  /// <returns>No content on success; a problem with the error otherwise.</returns>
  [HttpPost("Test/{channel}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Test(string channel, CancellationToken cancellationToken)
  {
    try
    {
      await _notificationService.SendTestAsync(channel, cancellationToken).ConfigureAwait(false);
      return NoContent();
    }
#pragma warning disable CA1031 // Surface any delivery error back to the admin.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      // SMTP failures wrap the real cause (cert/auth/connection) in inner exceptions — include them.
      var detail = ex.Message;
      for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
      {
        detail += " → " + inner.Message;
      }

      return Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
    }
  }
}
