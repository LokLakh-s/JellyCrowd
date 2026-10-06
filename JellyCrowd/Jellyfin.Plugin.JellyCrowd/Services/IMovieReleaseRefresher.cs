using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Keeps movie requests on their home release (see <see cref="MovieAvailability"/>). A movie still in
/// cinemas cannot be found yet, and its digital or physical date is often published, or moved, long after
/// it was requested.
/// </summary>
public interface IMovieReleaseRefresher
{
  /// <summary>
  /// Re-reads the release dates of every pending or approved movie request and reschedules the ones whose
  /// home release changed: a date ahead defers the request until then, a date passed makes it due; a movie
  /// with no date at all waits for one.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The number of requests rescheduled.</returns>
  Task<int> RefreshAsync(CancellationToken cancellationToken);
}
