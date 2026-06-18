using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Resolves the live download status of requests by querying the configured Radarr/Sonarr queue.
/// Only meaningful when the download backend is Servarr; returns nothing otherwise.
/// </summary>
public interface IServarrStatusService
{
  /// <summary>
  /// Returns the live download status for those of the given requests that are currently in a
  /// Radarr/Sonarr download queue. Requests with no queue entry are omitted.
  /// </summary>
  /// <param name="requests">The requests to look up (typically a single user's).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The statuses, keyed by request id.</returns>
  Task<IReadOnlyList<DownloadStatusDto>> GetStatusesAsync(IEnumerable<RequestRecord> requests, CancellationToken cancellationToken);
}
