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

  /// <summary>
  /// Best-effort full removal of a permanently-deleted, no-longer-owned title from the backend (movie
  /// from Radarr, whole series from Sonarr, including files + active downloads), so a future re-request
  /// starts clean. Never throws.
  /// </summary>
  /// <param name="request">The request whose media is being purged.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the backend confirms the title is gone (or there's nothing/no backend to
  /// purge); <c>false</c> when the purge failed so the deletion can be retried later.</returns>
  Task<bool> PurgeAsync(RequestRecord request, CancellationToken cancellationToken);

  /// <summary>
  /// Re-triggers a release search for an approved-but-blocked request (one that dispatched but never
  /// became available). Clears the stored dispatch error on success, records it again on failure.
  /// Never throws.
  /// </summary>
  /// <param name="request">The request to retry.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the retry was attempted successfully.</returns>
  Task<bool> RetryAsync(RequestRecord request, CancellationToken cancellationToken);

  /// <summary>
  /// Backstop that automatically re-searches approved requests that were dispatched but never became
  /// available (e.g. indexers were down, or the grab failed), throttled by a back-off and bounded by a
  /// max age so it gives up on genuinely-unavailable media. Never throws.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when stuck requests have been re-searched.</returns>
  Task RetryStuckAsync(CancellationToken cancellationToken);
}
