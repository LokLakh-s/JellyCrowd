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

  /// <summary>
  /// Initializes a new instance of the <see cref="RequestsController"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="quotaService">The quota service used to enforce per-user limits.</param>
  /// <param name="notificationService">The notification service.</param>
  public RequestsController(
    IRequestStore store,
    ICurrentUserAccessor userAccessor,
    IQuotaService quotaService,
    INotificationService notificationService)
  {
    _store = store;
    _userAccessor = userAccessor;
    _quotaService = quotaService;
    _notificationService = notificationService;
  }

  /// <summary>
  /// Creates a media request for the current user.
  /// </summary>
  /// <param name="dto">The request payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created request.</response>
  /// <response code="400">The payload was invalid.</response>
  /// <response code="403">The request would exceed the user's disk quota.</response>
  /// <response code="409">The user already has an active request for this title.</response>
  /// <response code="429">The user reached their request limit for the period.</response>
  /// <returns>The persisted request with its generated id and pending status.</returns>
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

    if (await _store.ExistsActiveAsync(userId, dto.TmdbId, dto.MediaType, dto.Season, cancellationToken).ConfigureAwait(false))
    {
      return Conflict("You already have an active request for this title.");
    }

    var config = Plugin.Instance?.Configuration;
    if (config is not null && config.MaxRequestsPerPeriod > 0)
    {
      var since = DateTime.UtcNow - PeriodToSpan(config.RequestPeriod);
      var recent = await _store.CountUserRequestsSinceAsync(userId, since, cancellationToken).ConfigureAwait(false);
      if (recent >= config.MaxRequestsPerPeriod)
      {
        return StatusCode(StatusCodes.Status429TooManyRequests, "You have reached your request limit for this period.");
      }
    }

    if (!await _quotaService.CanRequestAsync(userId, dto.MediaType, cancellationToken).ConfigureAwait(false))
    {
      return StatusCode(StatusCodes.Status403Forbidden, "This request would exceed your disk quota.");
    }

    var requireApproval = Plugin.Instance?.Configuration.RequireApproval ?? true;
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
        DesiredAt = dto.DesiredAt ?? DateTime.UtcNow,
        Status = requireApproval ? RequestStatus.Pending : RequestStatus.Approved
      },
      cancellationToken).ConfigureAwait(false);

    _ = _notificationService.NotifyRequestEventAsync(created, NotificationEvent.Created, CancellationToken.None);
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
  /// Cancels one of the current user's own requests, only while it is still pending.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The request was cancelled.</response>
  /// <response code="404">No matching pending request owned by the user.</response>
  /// <returns>No content on success; 404 otherwise.</returns>
  [HttpPost("{id}/Cancel")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var cancelled = await _store.CancelAsync(id, userId, cancellationToken).ConfigureAwait(false);
    return cancelled ? NoContent() : NotFound();
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
    return Ok(updated);
  }
}
