using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// A pluggable backend that fulfills an approved request (webhook POST, Radarr/Sonarr, …).
/// Jelly Crowd only emits the request; it never searches for or downloads files itself.
/// </summary>
public interface IDownloadClient
{
  /// <summary>
  /// Gets the backend identifier this client handles, matched against
  /// <see cref="PluginConfiguration.DownloadBackend"/> (e.g. <c>"webhook"</c>, <c>"servarr"</c>).
  /// </summary>
  string Backend { get; }

  /// <summary>
  /// Determines whether the client is sufficiently configured to dispatch.
  /// </summary>
  /// <param name="config">The current plugin configuration.</param>
  /// <returns><c>true</c> when the backend can be used.</returns>
  bool IsConfigured(PluginConfiguration config);

  /// <summary>
  /// Dispatches a request to the backend. Throws on failure so the caller can retry later.
  /// </summary>
  /// <param name="dispatch">The request payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the dispatch succeeds.</returns>
  Task DispatchAsync(DownloadDispatch dispatch, CancellationToken cancellationToken);

  /// <summary>
  /// Validates connectivity/configuration by performing a representative call. Throws on failure.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the test succeeds.</returns>
  Task TestAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Best-effort upstream cancellation when a user cancels an already-dispatched request (e.g. remove
  /// the movie from Radarr so it stops downloading). No-op for backends that can't undo a dispatch.
  /// </summary>
  /// <param name="dispatch">The original request payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when cancellation has been attempted.</returns>
  Task CancelAsync(DownloadDispatch dispatch, CancellationToken cancellationToken);

  /// <summary>
  /// Fully removes a title from the backend when it is permanently deleted and no longer owned by
  /// anyone: deletes the movie (Radarr) or the whole series (Sonarr) — including downloaded files — and
  /// removes any active downloads from the client (e.g. RDT). Unlike <see cref="CancelAsync"/>, this
  /// also removes Sonarr series, so re-requesting later starts from a clean slate. No-op for
  /// fire-and-forget backends. Never throws.
  /// </summary>
  /// <param name="dispatch">The original request payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the title is confirmed gone from the backend (or there was nothing to
  /// remove); <c>false</c> when the purge failed (e.g. the backend was unreachable) so the caller can retry.</returns>
  Task<bool> PurgeAsync(DownloadDispatch dispatch, CancellationToken cancellationToken);

  /// <summary>
  /// Re-triggers fulfillment for a request that was dispatched but never found a release ("blocked").
  /// For Radarr/Sonarr this runs a fresh search command on the already-added item (or adds it if it is
  /// missing); for fire-and-forget backends (webhook/script) it simply re-sends the dispatch. Throws on
  /// failure so the caller can surface the error.
  /// </summary>
  /// <param name="dispatch">The original request payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the retry has been attempted.</returns>
  Task RetryAsync(DownloadDispatch dispatch, CancellationToken cancellationToken);
}
