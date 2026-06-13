using System;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure rule deciding when an approved request is due to be dispatched to the download backend.
/// </summary>
public static class DownloadEligibility
{
  /// <summary>
  /// Determines whether a request should be dispatched now: it must be
  /// <see cref="RequestStatus.Approved"/>, not already dispatched, and its desired time (if any)
  /// must have arrived.
  /// </summary>
  /// <param name="request">The request to evaluate.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <returns><c>true</c> when the request is due for dispatch.</returns>
  public static bool IsDue(RequestRecord request, DateTime nowUtc)
  {
    ArgumentNullException.ThrowIfNull(request);

    return request.Status == RequestStatus.Approved
      && request.DispatchedAt is null
      && (request.DesiredAt is null || request.DesiredAt.Value <= nowUtc);
  }
}
