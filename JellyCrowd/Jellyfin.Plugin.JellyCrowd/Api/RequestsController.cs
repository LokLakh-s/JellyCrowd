using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// User media requests and the admin approval queue.
/// </summary>
[ApiController]
[Route("JellyCrowd/Requests")]
[Produces(MediaTypeNames.Application.Json)]
public class RequestsController : ControllerBase
{
  private readonly IRequestStore _store;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly IQuotaService _quotaService;
  private readonly INotificationService _notificationService;
  private readonly IDownloadDispatcher _downloadDispatcher;
  private readonly IServarrStatusService _servarrStatus;
  private readonly ILibraryMatcher _libraryMatcher;

  /// <summary>
  /// Initializes a new instance of the <see cref="RequestsController"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="quotaService">The quota service used to enforce per-user limits.</param>
  /// <param name="notificationService">The notification service.</param>
  /// <param name="downloadDispatcher">The download dispatcher triggered on approval.</param>
  /// <param name="servarrStatus">The live download-status service (Radarr/Sonarr queue).</param>
  /// <param name="libraryMatcher">The library matcher (resolves the Jellyfin item for a claim).</param>
  public RequestsController(
    IRequestStore store,
    ICurrentUserAccessor userAccessor,
    IQuotaService quotaService,
    INotificationService notificationService,
    IDownloadDispatcher downloadDispatcher,
    IServarrStatusService servarrStatus,
    ILibraryMatcher libraryMatcher)
  {
    _store = store;
    _userAccessor = userAccessor;
    _quotaService = quotaService;
    _notificationService = notificationService;
    _downloadDispatcher = downloadDispatcher;
    _servarrStatus = servarrStatus;
    _libraryMatcher = libraryMatcher;
  }

  /// <summary>
  /// Creates a media request for the current user.
  /// </summary>
  /// <param name="dto">The request payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created request (auto-approved, or pending when approval is required or the quota is exceeded).</response>
  /// <response code="400">The payload was invalid.</response>
  /// <response code="403">Requests are disabled for this user.</response>
  /// <response code="409">The user already has an active request for this title.</response>
  /// <response code="429">The user reached their request limit for the period.</response>
  /// <returns>The persisted request with its generated id and status.</returns>
  [HttpPost]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status403Forbidden)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
  public async Task<ActionResult<RequestRecord>> Create([FromBody] CreateRequestDto dto, CancellationToken cancellationToken)
  {
    if (dto is null || string.IsNullOrWhiteSpace(dto.Title))
    {
      return BadRequest("A title is required.");
    }

    if (!IsValidMediaType(dto.MediaType))
    {
      return BadRequest("The 'mediaType' must be 'movie' or 'tv'.");
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var config = Plugin.Instance?.Configuration;

    // Access control: an admin can disable requests for a given user.
    if (config is not null && !RequestPolicy.CanRequest(config, userId))
    {
      return StatusCode(StatusCodes.Status403Forbidden, "Requests are disabled for your account.");
    }

    if (await _store.ExistsActiveAsync(userId, dto.TmdbId, dto.MediaType, dto.Season, dto.Episode, cancellationToken).ConfigureAwait(false))
    {
      return Conflict("You already have an active request for this title.");
    }

    if (config is not null)
    {
      var cap = RequestPolicy.MaxRequestsPerPeriod(config, userId);
      if (cap > 0)
      {
        var since = DateTime.UtcNow - PeriodToSpan(config.RequestPeriod);
        var recent = await _store.CountUserRequestsSinceAsync(userId, since, cancellationToken).ConfigureAwait(false);
        if (recent >= cap)
        {
          return StatusCode(StatusCodes.Status429TooManyRequests, "You have reached your request limit for this period.");
        }
      }
    }

    // Approval mode: requests stay pending when admin approval is required, unless the user is trusted
    // or the request matches the auto-approval size rule. Even auto-approved requests are held as
    // pending (not rejected) when they would exceed the disk quota, for the admin to arbitrate.
    var requireApproval = (config?.RequireApproval ?? true)
      && !(config is not null && RequestPolicy.ShouldAutoApprove(config, userId, dto.MediaType));
    var withinQuota = await _quotaService.CanRequestAsync(userId, dto.MediaType, cancellationToken).ConfigureAwait(false);
    var status = (requireApproval || !withinQuota) ? RequestStatus.Pending : RequestStatus.Approved;

    var created = await _store.CreateAsync(
      new RequestRecord
      {
        UserId = userId,
        TmdbId = dto.TmdbId,
        MediaType = dto.MediaType,
        Title = dto.Title,
        PosterPath = dto.PosterPath,
        ReleaseDate = dto.ReleaseDate,
        Season = dto.Season,
        Episode = dto.Episode,
        DesiredAt = RequestScheduling.ResolveDesiredAt(dto.ReleaseDate, dto.DesiredAt, DateTime.UtcNow),
        Status = status
      },
      cancellationToken).ConfigureAwait(false);

    _ = _notificationService.NotifyRequestEventAsync(created, NotificationEvent.Created, CancellationToken.None);

    // Auto-approved requests are dispatched right away (no-op if not yet due / no backend configured).
    if (status == RequestStatus.Approved)
    {
      _ = _downloadDispatcher.DispatchAsync(created, CancellationToken.None);
    }

    return Ok(created);
  }

  /// <summary>
  /// Adds an already-available title to the current user's media (shared ownership). It counts toward
  /// the user's quota; the caller is expected to have shown the quota warning.
  /// </summary>
  /// <param name="dto">The title to claim (season/episode are ignored).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created available request.</response>
  /// <response code="400">Invalid payload, or the title is not in the library.</response>
  /// <response code="409">The user already owns this title.</response>
  /// <returns>The persisted available request.</returns>
  [HttpPost("Claim")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<RequestRecord>> Claim([FromBody] CreateRequestDto dto, CancellationToken cancellationToken)
  {
    if (dto is null || string.IsNullOrWhiteSpace(dto.Title))
    {
      return BadRequest("A title is required.");
    }

    if (!IsValidMediaType(dto.MediaType))
    {
      return BadRequest("The 'mediaType' must be 'movie' or 'tv'.");
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var itemId = _libraryMatcher.FindItemId(dto.MediaType, dto.TmdbId);
    if (string.IsNullOrEmpty(itemId))
    {
      return BadRequest("This title is not available in the library.");
    }

    if (await _store.ExistsActiveAsync(userId, dto.TmdbId, dto.MediaType, null, null, cancellationToken).ConfigureAwait(false))
    {
      return Conflict("This title is already in your media.");
    }

    var created = await _store.CreateAsync(
      new RequestRecord
      {
        UserId = userId,
        TmdbId = dto.TmdbId,
        MediaType = dto.MediaType,
        Title = dto.Title,
        PosterPath = dto.PosterPath,
        ReleaseDate = dto.ReleaseDate,
        Status = RequestStatus.Available,
        JellyfinItemId = itemId,
        AvailableAt = DateTime.UtcNow
      },
      cancellationToken).ConfigureAwait(false);

    return Ok(created);
  }

  /// <summary>
  /// Lists the current user's requests.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The user's requests.</response>
  /// <returns>The list of requests owned by the caller, newest first.</returns>
  [HttpGet("Mine")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<RequestRecord>>> Mine(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var items = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    return Ok(items);
  }

  /// <summary>
  /// Returns the live download status (Radarr/Sonarr queue) for the current user's approved requests.
  /// Requests that are not currently downloading are omitted; the list is empty unless the Servarr
  /// download backend is configured.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The download statuses, keyed by request id.</response>
  /// <returns>The list of live download statuses.</returns>
  [HttpGet("Mine/DownloadStatus")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<DownloadStatusDto>>> MineDownloadStatus(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var items = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    var statuses = await _servarrStatus.GetStatusesAsync(items, cancellationToken).ConfigureAwait(false);
    return Ok(statuses);
  }

  /// <summary>
  /// Lists all requests (administrators only).
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">All requests.</response>
  /// <returns>Every stored request, newest first.</returns>
  [HttpGet]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<RequestRecord>>> All(CancellationToken cancellationToken)
  {
    var items = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    return Ok(items);
  }

  /// <summary>
  /// Approves a request (administrators only).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The updated request.</response>
  /// <response code="404">No such request.</response>
  /// <returns>The request with its new approved status.</returns>
  [HttpPost("{id}/Approve")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public Task<ActionResult<RequestRecord>> Approve(Guid id, CancellationToken cancellationToken)
    => DecideAsync(id, RequestStatus.Approved, cancellationToken);

  /// <summary>
  /// Denies a request (administrators only).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The updated request.</response>
  /// <response code="404">No such request.</response>
  /// <returns>The request with its new denied status.</returns>
  [HttpPost("{id}/Deny")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public Task<ActionResult<RequestRecord>> Deny(Guid id, CancellationToken cancellationToken)
    => DecideAsync(id, RequestStatus.Denied, cancellationToken);

  /// <summary>
  /// Flags one of the current user's available titles for deletion (removed later by the scheduled task).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The request was flagged for deletion.</response>
  /// <response code="404">No matching available request owned by the user.</response>
  /// <returns>The updated request.</returns>
  [HttpPost("{id}/RequestDeletion")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<RequestRecord>> RequestDeletion(Guid id, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var updated = await _store.RequestDeletionAsync(id, userId, cancellationToken).ConfigureAwait(false);
    return updated is null ? NotFound() : Ok(updated);
  }

  /// <summary>
  /// Cancels a pending deletion (the user changed their mind), allowed while more than a minute
  /// remains before the deletion deadline.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The deletion flag was cleared.</response>
  /// <response code="404">No matching flagged request owned by the user.</response>
  /// <response code="409">Too late — the deletion is imminent.</response>
  /// <returns>The updated request.</returns>
  [HttpPost("{id}/CancelDeletion")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<RequestRecord>> CancelDeletion(Guid id, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var existing = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    if (existing is null || existing.UserId != userId || existing.DeletionRequestedAt is null)
    {
      return NotFound();
    }

    var retentionHours = Plugin.Instance?.Configuration.DeletionRetentionHours ?? 0;
    var deadline = existing.DeletionRequestedAt.Value.AddHours(retentionHours);
    if (deadline - DateTime.UtcNow <= TimeSpan.FromMinutes(1))
    {
      return Conflict("Too late to cancel — the deletion is imminent.");
    }

    var updated = await _store.CancelDeletionAsync(id, userId, cancellationToken).ConfigureAwait(false);
    return updated is null ? NotFound() : Ok(updated);
  }

  /// <summary>
  /// Cancels one of the current user's own requests while it is still pending or approved. When an
  /// approved request had been dispatched, the backend is asked to undo it (e.g. remove the movie
  /// from Radarr so it stops downloading).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The request was cancelled.</response>
  /// <response code="404">No matching pending/approved request owned by the user.</response>
  /// <returns>No content on success; 404 otherwise.</returns>
  [HttpPost("{id}/Cancel")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);

    // Propagate upstream before removing it locally, while we still have the record (only for an
    // approved request that may have been dispatched to a backend).
    var existing = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    if (existing is not null && existing.UserId == userId && existing.Status == RequestStatus.Approved)
    {
      await _downloadDispatcher.CancelAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    var cancelled = await _store.CancelAsync(id, userId, cancellationToken).ConfigureAwait(false);
    return cancelled ? NoContent() : NotFound();
  }

  /// <summary>
  /// Deletes any request (administrators only).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The request was deleted.</response>
  /// <response code="404">No such request.</response>
  /// <returns>No content on success; 404 otherwise.</returns>
  [HttpPost("{id}/Delete")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
  {
    var existing = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    if (existing is null)
    {
      return NotFound();
    }

    await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    return NoContent();
  }

  /// <summary>
  /// Edits a request's status, season/episode and desired date (administrators only).
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="dto">The edited values.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The updated request.</response>
  /// <response code="400">The payload was invalid.</response>
  /// <response code="404">No such request.</response>
  /// <returns>The request with its edited values, or 404.</returns>
  [HttpPost("{id}/Edit")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<RequestRecord>> Edit(Guid id, [FromBody] AdminEditRequestDto dto, CancellationToken cancellationToken)
  {
    if (dto is null)
    {
      return BadRequest("A payload is required.");
    }

    var updated = await _store.AdminUpdateAsync(id, dto.Status, dto.Season, dto.Episode, dto.DesiredAt, cancellationToken).ConfigureAwait(false);
    return updated is null ? NotFound() : Ok(updated);
  }

  /// <summary>
  /// Creates a request on behalf of another user (administrators only; bypasses quota/rate limits).
  /// </summary>
  /// <param name="dto">The request payload, including the target user.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created request.</response>
  /// <response code="400">The payload was invalid.</response>
  /// <returns>The persisted request.</returns>
  [HttpPost("ForUser")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RequestRecord>> CreateForUser([FromBody] AdminCreateRequestDto dto, CancellationToken cancellationToken)
  {
    if (dto is null || string.IsNullOrWhiteSpace(dto.Title))
    {
      return BadRequest("A title is required.");
    }

    if (!IsValidMediaType(dto.MediaType))
    {
      return BadRequest("The 'mediaType' must be 'movie' or 'tv'.");
    }

    if (dto.UserId == Guid.Empty)
    {
      return BadRequest("A target user is required.");
    }

    var status = dto.Status ?? RequestStatus.Approved;
    var created = await _store.CreateAsync(
      new RequestRecord
      {
        UserId = dto.UserId,
        TmdbId = dto.TmdbId,
        MediaType = dto.MediaType,
        Title = dto.Title,
        PosterPath = dto.PosterPath,
        ReleaseDate = dto.ReleaseDate,
        Season = dto.Season,
        Episode = dto.Episode,
        DesiredAt = RequestScheduling.ResolveDesiredAt(dto.ReleaseDate, null, DateTime.UtcNow),
        Status = status
      },
      cancellationToken).ConfigureAwait(false);

    _ = _notificationService.NotifyRequestEventAsync(created, NotificationEvent.Created, CancellationToken.None);
    if (status == RequestStatus.Approved)
    {
      _ = _downloadDispatcher.DispatchAsync(created, CancellationToken.None);
    }

    return Ok(created);
  }

  private static bool IsValidMediaType(string mediaType)
    => string.Equals(mediaType, "movie", StringComparison.Ordinal)
       || string.Equals(mediaType, "tv", StringComparison.Ordinal);

  private static TimeSpan PeriodToSpan(Models.RequestPeriod period) => period switch
  {
    Models.RequestPeriod.Day => TimeSpan.FromDays(1),
    Models.RequestPeriod.Month => TimeSpan.FromDays(30),
    _ => TimeSpan.FromDays(7)
  };

  private async Task<ActionResult<RequestRecord>> DecideAsync(Guid id, RequestStatus status, CancellationToken cancellationToken)
  {
    var adminId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var updated = await _store.UpdateStatusAsync(id, status, adminId, cancellationToken).ConfigureAwait(false);
    if (updated is null)
    {
      return NotFound();
    }

    var notificationEvent = status == RequestStatus.Approved ? NotificationEvent.Approved : NotificationEvent.Denied;
    _ = _notificationService.NotifyRequestEventAsync(updated, notificationEvent, CancellationToken.None);

    // On approval, hand the request to the configured download backend (no-op if none / not due).
    if (status == RequestStatus.Approved)
    {
      _ = _downloadDispatcher.DispatchAsync(updated, CancellationToken.None);
    }

    return Ok(updated);
  }
}
