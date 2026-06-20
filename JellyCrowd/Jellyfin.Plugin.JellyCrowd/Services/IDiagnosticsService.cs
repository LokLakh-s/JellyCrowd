using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Runs the plugin's health checks (TMDB, File Transformation, download backend, data folder) and
/// reports the on-disk footprint of its stores.
/// </summary>
public interface IDiagnosticsService
{
  /// <summary>
  /// Runs all checks and returns their results.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The diagnostic results.</returns>
  Task<IReadOnlyList<DiagnosticResult>> RunAsync(CancellationToken cancellationToken);
}
