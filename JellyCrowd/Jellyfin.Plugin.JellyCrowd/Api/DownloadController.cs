using System;
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
/// Admin endpoints to test the configured download backend and fetch Radarr/Sonarr resources.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("JellyCrowd/Download")]
[Produces(MediaTypeNames.Application.Json)]
public class DownloadController : ControllerBase
{
  private readonly IDownloadDispatcher _dispatcher;
  private readonly IServarrClient _servarr;

  /// <summary>
  /// Initializes a new instance of the <see cref="DownloadController"/> class.
  /// </summary>
  /// <param name="dispatcher">The download dispatcher.</param>
  /// <param name="servarr">The Radarr/Sonarr client used to list selectable resources.</param>
  public DownloadController(IDownloadDispatcher dispatcher, IServarrClient servarr)
  {
    _dispatcher = dispatcher;
    _servarr = servarr;
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

  /// <summary>
  /// Lists a Radarr/Sonarr instance's root folders and quality (and, for Sonarr, language) profiles,
  /// using credentials typed in the form, to populate the settings dropdowns.
  /// </summary>
  /// <param name="request">The instance service, URL and API key.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The available resources.</response>
  /// <response code="400">The instance is unreachable or the credentials are invalid.</response>
  /// <returns>The resources on success; a problem otherwise.</returns>
  [HttpPost("Servarr/Resources")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<ServarrResources>> ServarrResources([FromBody] ServarrResourcesRequest request, CancellationToken cancellationToken)
  {
    if (request is null || string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.ApiKey))
    {
      return BadRequest("The instance URL and API key are required.");
    }

    try
    {
      var includeLanguage = string.Equals(request.Service, "sonarr", StringComparison.OrdinalIgnoreCase);
      var resources = await _servarr.GetResourcesAsync(request.Url, request.ApiKey, includeLanguage, cancellationToken).ConfigureAwait(false);
      return Ok(resources);
    }
#pragma warning disable CA1031 // Surface any connection/auth error back to the admin.
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
