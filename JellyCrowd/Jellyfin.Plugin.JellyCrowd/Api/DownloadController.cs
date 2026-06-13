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
/// Admin endpoints to test the configured download backend.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("JellyCrowd/Download")]
[Produces(MediaTypeNames.Application.Json)]
public class DownloadController : ControllerBase
{
  private readonly IDownloadDispatcher _dispatcher;

  /// <summary>
  /// Initializes a new instance of the <see cref="DownloadController"/> class.
  /// </summary>
  /// <param name="dispatcher">The download dispatcher.</param>
  public DownloadController(IDownloadDispatcher dispatcher)
  {
    _dispatcher = dispatcher;
  }

  /// <summary>
  /// Exercises the currently selected download backend (save settings first).
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The backend test succeeded.</response>
  /// <response code="400">The backend failed or is not configured (details in the response).</response>
  /// <returns>No content on success; a problem with the error otherwise.</returns>
  [HttpPost("Test")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Test(CancellationToken cancellationToken)
  {
    try
    {
      await _dispatcher.TestActiveAsync(cancellationToken).ConfigureAwait(false);
      return NoContent();
    }
#pragma warning disable CA1031 // Surface any backend error back to the admin.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      var detail = ex.Message;
      for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
      {
        detail += " → " + inner.Message;
      }

      return Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
    }
  }
}
