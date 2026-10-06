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

  /// <summary>
  /// Determines whether a request whose dispatch failed is due another try. A failure is retried on every
  /// pass for its first hour (a backend restarting, a network blip), then hourly for a day, then every
  /// six hours: a title the backend cannot take (no TVDB match, say) no longer fails every few minutes.
  /// </summary>
  /// <param name="request">The request to evaluate.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <returns><c>true</c> when the request has not failed, or its back-off has elapsed.</returns>
  public static bool IsDispatchRetryDue(RequestRecord request, DateTime nowUtc)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (string.IsNullOrEmpty(request.DispatchError) || request.DispatchAttemptedAt is not { } lastAttempt)
    {
      return true;
    }

    var failingFor = nowUtc - (request.DispatchFailingSince ?? lastAttempt);
    var wait = failingFor < TimeSpan.FromHours(1) ? TimeSpan.Zero
      : failingFor < TimeSpan.FromDays(1) ? TimeSpan.FromHours(1)
      : TimeSpan.FromHours(6);
    return nowUtc - lastAttempt >= wait;
  }

  /// <summary>
  /// Determines whether a request is still waiting for its release: there is nothing to search for yet.
  /// </summary>
  /// <param name="request">The request to evaluate.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <returns><c>true</c> when the request's desired time is still ahead.</returns>
  public static bool IsAwaitingRelease(RequestRecord request, DateTime nowUtc)
  {
    ArgumentNullException.ThrowIfNull(request);
    return request.DesiredAt is { } desired && desired > nowUtc;
  }

  /// <summary>
  /// Gets the moment the backend could start looking for a request: its dispatch, or its release when that
  /// came later (an episode whose air date was only learned after it was sent). The "not found" window runs
  /// from here, not from the request — a title requested weeks before it came out, or held for approval or
  /// quota, would otherwise be given up on the instant it was sent.
  /// </summary>
  /// <param name="request">The request to evaluate.</param>
  /// <returns>The UTC moment the search started.</returns>
  public static DateTime SearchStartedAt(RequestRecord request)
  {
    ArgumentNullException.ThrowIfNull(request);
    var start = request.DispatchedAt ?? request.RequestedAt;
    return request.DesiredAt is { } desired && desired > start ? desired : start;
  }

  /// <summary>
  /// Determines whether a "not found" stamp should be withdrawn: it was set before the search window ran out
  /// (by the earlier rule that counted the window from the request, or before the release date moved later)
  /// and the window is still running, so the request deserves its full search. A stamp whose window has
  /// elapsed since stays, so the requester is not warned a second time.
  /// </summary>
  /// <param name="request">The request to evaluate.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="searchWindow">How long a request is searched for before it is reported not found.</param>
  /// <returns><c>true</c> when the stamp is premature and the search should resume.</returns>
  public static bool IsNotFoundPremature(RequestRecord request, DateTime nowUtc, TimeSpan searchWindow)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (request.NotFoundNotifiedAt is not { } stamped)
    {
      return false;
    }

    var start = SearchStartedAt(request);
    return stamped - start < searchWindow && nowUtc - start <= searchWindow;
  }
}
