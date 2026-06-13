using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Computes per-user disk usage and enforces quotas.
/// </summary>
public interface IQuotaService
{
  /// <summary>
  /// Gets the quota (in bytes) for a user: their override if any, otherwise the global default. 0 means unlimited.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <returns>The quota in bytes.</returns>
  long GetQuotaBytes(Guid userId);

  /// <summary>
  /// Gets the current usage snapshot for a user (actual bytes used by fulfilled requests vs quota).
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The usage snapshot.</returns>
  Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Determines whether the user can make a new request of the given media type without exceeding their quota.
  /// Considers actual used bytes plus estimates for in-flight requests and the new one.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="mediaType">The media type being requested (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the request fits within the quota.</returns>
  Task<bool> CanRequestAsync(Guid userId, string mediaType, CancellationToken cancellationToken);
}
