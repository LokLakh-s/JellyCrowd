using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Persistence for media requests.
/// </summary>
public interface IRequestStore
{
  /// <summary>
  /// Creates a new request (assigns its id, timestamp and <see cref="RequestStatus.Pending"/> status).
  /// </summary>
  /// <param name="record">The request to create.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The stored request.</returns>
  Task<RequestRecord> CreateAsync(RequestRecord record, CancellationToken cancellationToken);

  /// <summary>
  /// Gets every request, newest first.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>All requests.</returns>
  Task<IReadOnlyList<RequestRecord>> GetAllAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Gets the requests made by a given user, newest first.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The user's requests.</returns>
  Task<IReadOnlyList<RequestRecord>> GetByUserAsync(Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Gets a single request by id.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The request, or <c>null</c> if not found.</returns>
  Task<RequestRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

  /// <summary>
  /// Updates the status of a request and stamps the deciding administrator and time.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="status">The new status.</param>
  /// <param name="decidedBy">The administrator making the decision.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found.</returns>
  Task<RequestRecord?> UpdateStatusAsync(Guid id, RequestStatus status, Guid decidedBy, CancellationToken cancellationToken);

  /// <summary>
  /// Promotes a request that is still <see cref="RequestStatus.Pending"/> and flagged
  /// <see cref="RequestRecord.HeldForQuota"/> to <see cref="RequestStatus.Approved"/>, clearing the flag.
  /// A no-op (returns <c>null</c>) if the request no longer exists, is no longer pending, or is no longer
  /// quota-held — so concurrent admin decisions are never overwritten.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The promoted request, or <c>null</c> when it was not eligible.</returns>
  Task<RequestRecord?> PromoteFromQuotaHoldAsync(Guid id, CancellationToken cancellationToken);

  /// <summary>
  /// Puts an <see cref="RequestStatus.Approved"/>, not-yet-dispatched request back on a quota hold
  /// (<see cref="RequestStatus.Pending"/> + <see cref="RequestRecord.HeldForQuota"/>) — used when a title
  /// becomes due but no longer fits the user's quota. The <see cref="IQuotaHoldPromoter"/> resumes it once
  /// space frees. A no-op (returns <c>null</c>) if it is not approved or has already been dispatched.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The held request, or <c>null</c> when it was not eligible.</returns>
  Task<RequestRecord?> HoldForQuotaAsync(Guid id, CancellationToken cancellationToken);

  /// <summary>
  /// Determines whether the user already has a non-denied request for the same title.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <param name="mediaType">The media type.</param>
  /// <param name="season">The season number (null for movies/whole show).</param>
  /// <param name="episode">The episode number (null for movies/whole season).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when an active duplicate exists.</returns>
  Task<bool> ExistsActiveAsync(Guid userId, int tmdbId, string mediaType, int? season, int? episode, CancellationToken cancellationToken);

  /// <summary>
  /// Counts the user's non-denied requests created at or after the given UTC instant (for rate limiting).
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="sinceUtc">The window start (UTC).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The number of requests in the window.</returns>
  Task<int> CountUserRequestsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken);

  /// <summary>
  /// Marks a request available and records the matching Jellyfin library item id.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="jellyfinItemId">The Jellyfin library item id (32-char hex).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found.</returns>
  Task<RequestRecord?> MarkAvailableAsync(Guid id, string jellyfinItemId, CancellationToken cancellationToken);

  /// <summary>
  /// Re-points an already-available request at its library item, without touching its status or its
  /// ownership clock. A library rescan or metadata refresh regenerates Jellyfin item ids, leaving the
  /// stored one dangling — deletion and the "open in Jellyfin" link would then miss.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="jellyfinItemId">The current Jellyfin library item id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found.</returns>
  Task<RequestRecord?> SetJellyfinItemIdAsync(Guid id, string jellyfinItemId, CancellationToken cancellationToken);

  /// <summary>
  /// Resets an available request's ownership clock (its <see cref="RequestRecord.AvailableAt"/>) to the
  /// given time, restarting the expiry countdown ("renew" / re-claim).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="whenUtc">The new ownership timestamp (usually now).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found.</returns>
  Task<RequestRecord?> RenewAvailableAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken);

  /// <summary>
  /// Removes ownerships (available requests, not pending deletion) that became available before
  /// <paramref name="cutoffUtc"/>, i.e. whose expiry window has elapsed. Only the ownership record is
  /// removed — the media file is never deleted here (deletion stays on-demand).
  /// </summary>
  /// <param name="cutoffUtc">Ownerships with <see cref="RequestRecord.AvailableAt"/> before this lapse.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The ownership records that lapsed (so their owners can be notified).</returns>
  Task<IReadOnlyList<RequestRecord>> ExpireOwnershipsAsync(DateTime cutoffUtc, CancellationToken cancellationToken);

  /// <summary>
  /// Cancels (removes) one of the user's own requests, only while it is still pending.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="userId">The owner (must match).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the request was cancelled; <c>false</c> if not found, not owned, or not pending.</returns>
  Task<bool> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Flags one of the user's available requests for deletion (sets the deletion timestamp).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="userId">The owner (must match).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found, not owned, or not available.</returns>
  Task<RequestRecord?> RequestDeletionAsync(Guid id, Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Flags an available request's media for deletion regardless of owner (administrator action). The
  /// scheduled task purges the backend and removes the files after the retention period; cancellable until
  /// then. Shared media is only physically removed once no other active request still wants it.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found, not available, or already flagged.</returns>
  Task<RequestRecord?> AdminFlagDeletionAsync(Guid id, CancellationToken cancellationToken);

  /// <summary>
  /// Clears a pending deletion flag on one of the user's available requests (the user changed their mind).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="userId">The owner (must match).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found, not owned, or not flagged for deletion.</returns>
  Task<RequestRecord?> CancelDeletionAsync(Guid id, Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Determines whether another request (different id, not flagged for deletion, not denied) still
  /// references the same media — i.e. another user still wants content that overlaps this scope. Matching is
  /// per season/episode (see <see cref="MediaScope.Overlaps"/>): deleting one season is not blocked by
  /// another season of the same series still being owned.
  /// </summary>
  /// <param name="excludeId">The request to exclude (the one being deleted).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <param name="mediaType">The media type.</param>
  /// <param name="season">The season being deleted (<c>null</c> = movie or whole series).</param>
  /// <param name="episode">The episode being deleted (<c>null</c> = whole season).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when another active reference overlaps this scope.</returns>
  Task<bool> AnyActiveReferenceAsync(Guid excludeId, int tmdbId, string mediaType, int? season, int? episode, CancellationToken cancellationToken);

  /// <summary>
  /// Gets requests whose deletion was requested at or before the given cutoff (retention elapsed).
  /// </summary>
  /// <param name="cutoffUtc">The cutoff instant (UTC).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The requests due for deletion.</returns>
  Task<IReadOnlyList<RequestRecord>> GetDueForDeletionAsync(DateTime cutoffUtc, CancellationToken cancellationToken);

  /// <summary>
  /// Removes a request from the store.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the request has been removed.</returns>
  Task DeleteAsync(Guid id, CancellationToken cancellationToken);

  /// <summary>
  /// Admin edit of a request's status, season/episode and desired date.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="status">The new status.</param>
  /// <param name="season">The season number (null = movie/whole show).</param>
  /// <param name="episode">The episode number (null = whole season/movie).</param>
  /// <param name="desiredAt">The desired (UTC) fulfillment time.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found.</returns>
  Task<RequestRecord?> AdminUpdateAsync(Guid id, RequestStatus status, int? season, int? episode, DateTime? desiredAt, CancellationToken cancellationToken);

  /// <summary>
  /// Stamps the time a request was dispatched to the download backend (idempotency marker).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="whenUtc">The dispatch time (UTC).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found.</returns>
  Task<RequestRecord?> MarkDispatchedAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken);

  /// <summary>
  /// Records the outcome of a dispatch attempt: stamps the attempt time and sets (or clears, when
  /// <paramref name="error"/> is <c>null</c>) the last dispatch error for admin diagnosis.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="error">The failure message, or <c>null</c> to clear it.</param>
  /// <param name="whenUtc">The attempt time (UTC).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found.</returns>
  Task<RequestRecord?> SetDispatchErrorAsync(Guid id, string? error, DateTime whenUtc, CancellationToken cancellationToken);

  /// <summary>
  /// Stamps the moment the requester was told the media could not be found, so the warning is sent once.
  /// Returns <c>null</c> when the request is gone or was already stamped.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="whenUtc">The moment the warning was sent (UTC).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found or already notified.</returns>
  Task<RequestRecord?> MarkNotFoundNotifiedAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken);

  /// <summary>
  /// Gets approved requests that are due for download dispatch: not yet dispatched and whose desired
  /// time (if any) is at or before the given instant.
  /// </summary>
  /// <param name="nowUtc">The current instant (UTC).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The requests due for dispatch.</returns>
  Task<IReadOnlyList<RequestRecord>> GetDueForDispatchAsync(DateTime nowUtc, CancellationToken cancellationToken);
}
