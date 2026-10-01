using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// What happens once requests become available, beyond the "available" notice: the subtitles their
/// requesters asked for are fetched, and a requester whose preferred version is missing is told so.
/// </summary>
public interface IAvailabilityFollowUp
{
  /// <summary>
  /// Schedules the follow-up of requests that just became available. Returns at once; the work runs in
  /// the background a little later, once Jellyfin has probed the new files.
  /// </summary>
  /// <param name="available">The requests that just became available.</param>
  void Schedule(IReadOnlyList<RequestRecord> available);
}
