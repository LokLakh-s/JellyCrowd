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
  /// Clears a pending deletion flag on one of the user's available requests (the user changed their mind).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="userId">The owner (must match).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated request, or <c>null</c> if not found, not owned, or not flagged for deletion.</returns>
  Task<RequestRecord?> CancelDeletionAsync(Guid id, Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Determines whether another request (different id, not flagged for deletion, not denied) still
  /// references the same title — i.e. another user/season still wants the media.
  /// </summary>
  /// <param name="excludeId">The request to exclude (the one being deleted).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <param name="mediaType">The media type.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when another active reference exists.</returns>
  Task<bool> AnyActiveReferenceAsync(Guid excludeId, int tmdbId, string mediaType, CancellationToken cancellationToken);

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
  /// Gets approved requests that are due for download dispatch: not yet dispatched and whose desired
  /// time (if any) is at or before the given instant.
  /// </summary>
  /// <param name="nowUtc">The current instant (UTC).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The requests due for dispatch.</returns>
  Task<IReadOnlyList<RequestRecord>> GetDueForDispatchAsync(DateTime nowUtc, CancellationToken cancellationToken);
}
