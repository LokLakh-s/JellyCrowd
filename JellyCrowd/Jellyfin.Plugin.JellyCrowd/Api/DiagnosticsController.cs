using System.Collections.Generic;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Admin health checks (TMDB, File Transformation, download backend, data folder, footprint).
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("JellyCrowd/Diagnostics")]
[Produces(MediaTypeNames.Application.Json)]
public class DiagnosticsController : ControllerBase
{
  private readonly IDiagnosticsService _diagnostics;

  /// <summary>
  /// Initializes a new instance of the <see cref="DiagnosticsController"/> class.
  /// </summary>
  /// <param name="diagnostics">The diagnostics service.</param>
  public DiagnosticsController(IDiagnosticsService diagnostics)
  {
    _diagnostics = diagnostics;
  }

  /// <summary>
  /// Runs all health checks.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The diagnostic results.</response>
  /// <returns>The list of check results.</returns>
  [HttpGet]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<DiagnosticResult>>> Run(CancellationToken cancellationToken)
  {
    return Ok(await _diagnostics.RunAsync(cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Downloads a JSON backup of the data stores (requests, watchlist, notifications, preferences).
  /// Config is excluded, so the file carries no secrets.
  /// </summary>
  /// <response code="200">The backup file.</response>
  /// <returns>A JSON file attachment.</returns>
  [HttpGet("Export")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public IActionResult Export()
  {
    var bundle = ExportBundle.Build(Plugin.Instance!.DataFolderPath);
    var bytes = Encoding.UTF8.GetBytes(bundle.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return File(bytes, MediaTypeNames.Application.Json, "jellycrowd-backup.json");
  }
}
