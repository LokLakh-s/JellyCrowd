using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IQuotaHoldPromoter"/>. Run whenever a user's committed footprint can drop (media
/// expired/deleted, a request fulfilled at a smaller real size, or a request denied/cancelled): it
/// resumes that user's quota-held requests once they are back within quota.
/// </summary>
public sealed class QuotaHoldPromoter : IQuotaHoldPromoter
{
  private readonly IRequestStore _store;
  private readonly IQuotaService _quotaService;
  private readonly IDownloadDispatcher _downloadDispatcher;
  private readonly INotificationService _notificationService;
  private readonly ILogger<QuotaHoldPromoter> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="QuotaHoldPromoter"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="quotaService">The quota service (to test whether a user is back within quota).</param>
  /// <param name="downloadDispatcher">The download dispatcher (to fulfill a resumed request).</param>
  /// <param name="notificationService">The notification service (to tell the requester it resumed).</param>
  /// <param name="logger">The logger.</param>
  public QuotaHoldPromoter(
    IRequestStore store,
    IQuotaService quotaService,
    IDownloadDispatcher downloadDispatcher,
    INotificationService notificationService,
    ILogger<QuotaHoldPromoter> logger)
  {
    _store = store;
    _quotaService = quotaService;
    _downloadDispatcher = downloadDispatcher;
    _notificationService = notificationService;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<int> PromoteAsync(CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var held = all.Where(r => r.Status == RequestStatus.Pending && r.HeldForQuota).ToList();
    if (held.Count == 0)
    {
      return 0;
    }

    var promoted = 0;
    foreach (var group in held.GroupBy(r => r.UserId))
    {
      cancellationToken.ThrowIfCancellationRequested();

      // A held request is already counted in the user's committed footprint, so promoting it does not
      // change that footprint: if the user is now within quota, all of their held requests fit at once.
      if (!await _quotaService.IsWithinQuotaAsync(group.Key, cancellationToken).ConfigureAwait(false))
      {
        continue;
      }

      // Oldest first, so resumption respects the order requests were made.
      foreach (var request in group.OrderBy(r => r.RequestedAt))
      {
        var updated = await _store.PromoteFromQuotaHoldAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (updated is null)
        {
          continue; // raced with an admin decision or another promotion — leave it be
        }

        await _notificationService.NotifyRequestEventAsync(updated, NotificationEvent.Approved, cancellationToken).ConfigureAwait(false);
        _ = _downloadDispatcher.DispatchAsync(updated, CancellationToken.None);
        promoted++;
      }
    }

    if (promoted > 0)
    {
      _logger.LogInformation("Jelly Crowd quota-hold: resumed {Count} request(s) after quota freed up.", promoted);
    }

    return promoted;
  }
}
