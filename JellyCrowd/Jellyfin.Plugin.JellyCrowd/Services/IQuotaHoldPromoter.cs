using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Promotes requests that were held back solely by a user's disk quota (see
/// <see cref="Models.RequestRecord.HeldForQuota"/>) once that user's quota frees up, so they resume
/// automatically without ever entering the admin approval queue.
/// </summary>
public interface IQuotaHoldPromoter
{
  /// <summary>
  /// Scans every quota-held request and, for each user now back within their quota, promotes all of their
  /// held requests to <see cref="Models.RequestStatus.Approved"/>, notifies the requester and dispatches them.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The number of requests promoted.</returns>
  Task<int> PromoteAsync(CancellationToken cancellationToken);
}
