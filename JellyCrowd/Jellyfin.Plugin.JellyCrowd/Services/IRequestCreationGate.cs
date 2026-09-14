using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Serializes request creation per user, so checking what a user already asked for and recording the new
/// request happen as one step. Without it, requests sent within the same instant (a season and its series
/// clicked in a row) each saw the other as absent and were both accepted.
/// </summary>
public interface IRequestCreationGate
{
  /// <summary>
  /// Waits for exclusive creation rights for a user. Dispose the result to release them.
  /// </summary>
  /// <param name="userId">The user the request is created for.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A handle releasing the user's gate when disposed.</returns>
  Task<IDisposable> EnterAsync(Guid userId, CancellationToken cancellationToken);
}
