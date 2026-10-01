using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Keeps per-episode requests on the air dates TMDB lists for their episodes. TMDB often fills in the air
/// dates of a new season only days after its first episode is announced, and moves them now and then: a
/// request keeps whatever date it was made with unless something brings it up to date.
/// </summary>
public interface IEpisodeAirDateRefresher
{
  /// <summary>
  /// Re-reads the air date of every pending or approved per-episode request and reschedules the ones whose
  /// date changed: a date still ahead defers the request until then, a date already passed makes it due.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The number of requests rescheduled.</returns>
  Task<int> RefreshAsync(CancellationToken cancellationToken);
}
