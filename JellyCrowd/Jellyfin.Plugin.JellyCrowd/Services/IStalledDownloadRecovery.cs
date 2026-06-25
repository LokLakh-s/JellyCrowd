using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Detects downloads that have stalled (no progress past a threshold) and recovers them by blocklisting
/// the dead grab and re-searching, so Radarr/Sonarr fetch a different release. Opt-in; Radarr/Sonarr only.
/// </summary>
public interface IStalledDownloadRecovery
{
  /// <summary>
  /// Scans approved requests' download queue and recovers any that have been stalled past the configured
  /// threshold. No-op when disabled or when the backend isn't Radarr/Sonarr. Never throws.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the scan finishes.</returns>
  Task RecoverAsync(CancellationToken cancellationToken);
}
