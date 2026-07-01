using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// One-time import of historical playback from the Playback Reporting plugin's SQLite database.
/// </summary>
public interface IPlaybackReportingImporter
{
  /// <summary>
  /// Reads the Playback Reporting database (when present) and merges its history into the playback store,
  /// skipping anything that overlaps Jelly Crowd's own captured range so nothing is double-counted.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The import outcome.</returns>
  Task<ImportResultDto> ImportAsync(CancellationToken cancellationToken);
}
