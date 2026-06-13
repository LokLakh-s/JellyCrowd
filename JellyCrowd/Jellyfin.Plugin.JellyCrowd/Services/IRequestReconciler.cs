using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Flips approved requests to available once the matching media exists in the library.
/// </summary>
public interface IRequestReconciler
{
  /// <summary>
  /// Reconciles all approved requests against the library.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The number of requests that became available.</returns>
  Task<int> ReconcileAsync(CancellationToken cancellationToken);
}
