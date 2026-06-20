using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Orchestrates handing approved requests to the configured download backend.
/// </summary>
public interface IDownloadDispatcher
{
  /// <summary>
  /// Dispatches a single request if it is due (approved, not yet dispatched, desired time reached)
  /// and a backend is configured. Safe to fire-and-forget; never throws.
  /// </summary>
  /// <param name="request">The request to consider.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the request was dispatched.</returns>
  Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken);

  /// <summary>
  /// Dispatches every request currently due (used by the scheduled backstop and for deferred
  /// desired dates).
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when all due requests have been processed.</returns>
  Task DispatchDueAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Exercises the currently selected backend so the admin can validate its configuration. Throws
  /// with a descriptive message on failure.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the test succeeds.</returns>
  Task TestActiveAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Best-effort upstream cancellation of an already-dispatched request (e.g. remove it from Radarr
  /// so it stops downloading). Never throws.
  /// </summary>
  /// <param name="request">The request being cancelled.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when cancellation has been attempted.</returns>
  Task CancelAsync(RequestRecord request, CancellationToken cancellationToken);
}
