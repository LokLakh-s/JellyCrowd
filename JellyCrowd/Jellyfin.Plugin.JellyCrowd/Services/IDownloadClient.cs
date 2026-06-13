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
}
