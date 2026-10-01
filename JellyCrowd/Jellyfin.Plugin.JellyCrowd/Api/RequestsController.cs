using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class RequestsController : ControllerBase
{
  private readonly IRequestStore _store;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly IQuotaService _quotaService;
  private readonly INotificationService _notificationService;
  private readonly IDownloadDispatcher _downloadDispatcher;
  private readonly IServarrStatusService _servarrStatus;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly ITmdbClient _tmdbClient;
  private readonly IActivityLog _activityLog;
  private readonly IRequestCreationGate _creationGate;
  private readonly IContentRestrictionService _restrictions;
  private readonly IRequestCreationService _creator;
  private readonly Func<Guid, string> _resolveUserName;

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
  /// <param name="tmdbClient">The TMDB client (resolves genres for genre-based auto-approval).</param>
  /// <param name="activityLog">The activity log (records user actions).</param>
  /// <param name="creationGate">Serializes request creation per user, so duplicate checks cannot race.</param>
  /// <param name="restrictions">Refuses titles above the user's parental restriction.</param>
  /// <param name="creator">Creates requests (the user's own, and on a user's behalf).</param>
  /// <param name="resolveUserName">Resolves a user id to a display name for log messages.</param>
  public RequestsController(
    IRequestStore store,
    ICurrentUserAccessor userAccessor,
    IQuotaService quotaService,
    INotificationService notificationService,
    IDownloadDispatcher downloadDispatcher,
    IServarrStatusService servarrStatus,
    ILibraryMatcher libraryMatcher,
    ITmdbClient tmdbClient,
    IActivityLog activityLog,
    IRequestCreationGate creationGate,
    IContentRestrictionService restrictions,
    IRequestCreationService creator,
    Func<Guid, string> resolveUserName)
  {
    _store = store;
    _userAccessor = userAccessor;
    _quotaService = quotaService;
    _notificationService = notificationService;
    _downloadDispatcher = downloadDispatcher;
    _servarrStatus = servarrStatus;
    _libraryMatcher = libraryMatcher;
    _tmdbClient = tmdbClient;
    _activityLog = activityLog;
    _creationGate = creationGate;
    _restrictions = restrictions;
    _creator = creator;
    _resolveUserName = resolveUserName;
  }

  /// <summary>
  /// Creates a media request for the current user.
  /// </summary>
  /// <param name="dto">The request payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created request (auto-approved, or pending when approval is required or the quota is exceeded).</response>
  /// <response code="400">The payload was invalid.</response>
  /// <response code="403">Requests are disabled for this user, or the title is above their parental restriction.</response>
  /// <response code="409">The user already has this title, or it is already on its way.</response>
  /// <response code="422">The request is larger than the user's whole disk quota.</response>
  /// <response code="429">The user reached their request limit for the period.</response>
  /// <response code="503">The title's rating could not be checked for a user under a parental restriction.</response>
  /// <returns>The persisted request with its generated id and status.</returns>
  [HttpPost]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status403Forbidden)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
  [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<RequestRecord>> Create([FromBody] CreateRequestDto dto, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    return ToActionResult(await _creator.CreateAsync(userId, dto, cancellationToken).ConfigureAwait(false));
  }

  // The library item a claim or an assignment takes ownership of: an episode, a season, or the whole
  // movie/series. A season must hold an episode — Jellyfin keeps the season folder after its files are
  // deleted, and that empty shell is not something to own (FindItemId applies the same rule to a series).
  private string? FindOwnableItemId(string mediaType, int tmdbId, int? season, int? episode)
  {
    if (!string.Equals(mediaType, "tv", StringComparison.Ordinal) || season is not int seasonNumber)
    {
      return _libraryMatcher.FindItemId(mediaType, tmdbId);
    }

    if (episode is int episodeNumber)
    {
      return _libraryMatcher.FindEpisodeItemId(tmdbId, seasonNumber, episodeNumber);
    }

    return _libraryMatcher.FindEpisodeItemId(tmdbId, seasonNumber, null) is null
      ? null
      : _libraryMatcher.FindSeasonItemId(tmdbId, seasonNumber);
  }

  // Whether a title already on disk still fits in what is left of the user's quota: their committed
  // footprint (owned titles at their real size plus what in-flight requests reserve) plus this title's
  // own size on disk. A user already over their quota fails this whatever the size — including the 0 a
  // library lookup returns when it cannot measure the item, which must not become a free pass.
  private async Task<bool> FitsInQuotaAsync(Guid userId, string mediaType, int tmdbId, int? season, int? episode, CancellationToken cancellationToken)
  {
    var quota = _quotaService.GetQuotaBytes(userId);
    if (quota <= 0)
    {
      return true;
    }

    var committed = await _quotaService.GetCommittedBytesAsync(userId, cancellationToken).ConfigureAwait(false);
    return committed + _libraryMatcher.GetSizeBytes(mediaType, tmdbId, season, episode) <= quota;
  }

  /// <summary>
  /// Adds an already-available title to the current user's media (shared ownership). It counts toward
  /// the user's quota at its real size on disk, and is refused when it does not fit — unlike a request,
  /// a claim has nothing to wait for, so there is no point holding it: the space is needed now. Re-claiming
  /// a title already owned renews it instead, which is refused too while the user is over their quota.
  /// </summary>
  /// <param name="dto">The title to claim; an optional <c>Season</c> claims just that season of a show.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created available request.</response>
  /// <response code="400">Invalid payload, or the title is not in the library.</response>
  /// <response code="403">The media type is off, or the title is above the user's parental restriction.</response>
  /// <response code="409">The user already owns this title.</response>
  /// <response code="422">The title does not fit in what is left of the user's disk quota, or the user is
  /// over their quota and may no longer renew an ownership.</response>
  /// <response code="503">The title's rating could not be checked for a user under a parental restriction.</response>
  /// <returns>The persisted available request.</returns>
  [HttpPost("Claim")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status403Forbidden)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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

    if (Plugin.Instance?.Configuration is { } claimConfig && !RequestPolicy.IsMediaTypeEnabled(claimConfig, dto.MediaType))
    {
      return StatusCode(StatusCodes.Status403Forbidden, "This media type is not available on this server.");
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);

    // A child account owns nothing by itself: a parent requests for them (Requests/ForChild).
    if (Plugin.Instance?.Configuration is { } childConfig && ChildAccountPolicy.IsChild(childConfig, userId))
    {
      return StatusCode(StatusCodes.Status403Forbidden, "Child accounts don't add titles: a parent requests for them.");
    }

    if (await CheckParentalRestrictionAsync(userId, dto.MediaType, dto.TmdbId, cancellationToken).ConfigureAwait(false) is { } refused)
    {
      return refused;
    }

    // A claim can be scoped to one season (the "Add to my library" button on a season page): resolve and
    // own that season's item, otherwise the whole movie/series.
    var itemId = FindOwnableItemId(dto.MediaType, dto.TmdbId, dto.Season, dto.Episode);
    if (string.IsNullOrEmpty(itemId))
    {
      return BadRequest("This title is not available in the library.");
    }

    // Already owned — this exact scope, or a broader one that covers it (e.g. the whole series covers a
    // season) → renew the ownership (resets the expiry countdown) rather than duplicating.
    using var creation = await _creationGate.EnterAsync(userId, cancellationToken).ConfigureAwait(false);
    var mine = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    var owned = mine.FirstOrDefault(r =>
      r.TmdbId == dto.TmdbId
      && string.Equals(r.MediaType, dto.MediaType, StringComparison.Ordinal)
      && r.Status == RequestStatus.Available
      && MediaScope.Overlaps(dto.Season, dto.Episode, r.Season, r.Episode));
    if (owned is not null)
    {
      // Renewing adds no bytes, but a library that has grown PAST its quota has to shrink: while the user
      // is over, ownerships stop being renewable, so the expiry prunes what they do not free themselves.
      // Strictly over — at the quota, or near it, renewing still works. Lapsing only drops the ownership
      // (and its quota charge); the file stays in the shared library, so nothing is destroyed by waiting.
      if (await _quotaService.IsOverQuotaAsync(userId, cancellationToken).ConfigureAwait(false))
      {
        return UnprocessableEntity("Your library is over your disk quota. Free some space before renewing.");
      }

      var renewed = await _store.RenewAvailableAsync(owned.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
      return Ok(renewed);
    }

    // Claiming an already-downloaded title charges its real size on disk to the quota, so it is gated
    // like a request — it just was not, which let a user grow past their quota one already-present title
    // at a time (nothing else adds owned bytes without passing a gate). The size is known exactly here,
    // the file being on disk, so no estimate is involved.
    if (!await FitsInQuotaAsync(userId, dto.MediaType, dto.TmdbId, dto.Season, dto.Episode, cancellationToken).ConfigureAwait(false))
    {
      return UnprocessableEntity("Adding this title would exceed your disk quota. Free some space first.");
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
        Season = dto.Season,
        Episode = dto.Episode,
        Status = RequestStatus.Available,
        JellyfinItemId = itemId,
        AvailableAt = DateTime.UtcNow
      },
      cancellationToken).ConfigureAwait(false);

    _ = _activityLog.LogAsync("info", "user", _resolveUserName(userId) + " added " + created.Title + " to their library", _resolveUserName(userId), CancellationToken.None);
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
  /// Lists who currently owns which media (administrators only): every available title grouped by its
  /// exact scope (movie, or a TV season/episode), with the owning users.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">Media ownerships, by title.</response>
  /// <returns>The ownership map.</returns>
  [HttpGet("Ownerships")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<MediaOwnershipDto>>> Ownerships(CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);

    var result = all
      .Where(r => r.Status == RequestStatus.Available)
      .GroupBy(r => r.MediaType + ":" + r.TmdbId + ":" + (r.Season?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*")
        + ":" + (r.Episode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*"))
      .Select(g =>
      {
        var first = g.First();
        var dto = new MediaOwnershipDto
        {
          MediaType = first.MediaType,
          TmdbId = first.TmdbId,
          Title = first.Title,
          PosterPath = first.PosterPath,
          Season = first.Season,
          Episode = first.Episode
        };
        foreach (var owner in g
          .GroupBy(r => r.UserId)
          .Select(u => new OwnerDto { Name = _resolveUserName(u.Key), SinceUtc = u.Max(r => r.AvailableAt) })
          .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
          dto.Owners.Add(owner);
        }

        return dto;
      })
      .OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
      .ThenBy(d => d.Season ?? 0)
      .ThenBy(d => d.Episode ?? 0)
      .ToList();

    return Ok(result);
  }

  /// <summary>
  /// Gets every request for a single title (administrators only) so the media detail popup can show who
  /// wanted it and in what state. Newest first.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The title's requests.</response>
  /// <returns>The requests for the title.</returns>
  [HttpGet("Media/{mediaType}/{tmdbId:int}")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<MediaAdminRequestDto>>> Media(string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var result = all
      .Where(r => r.TmdbId == tmdbId && string.Equals(r.MediaType, mediaType, StringComparison.OrdinalIgnoreCase))
      .OrderByDescending(r => r.RequestedAt)
      .Select(r => new MediaAdminRequestDto
      {
        UserName = _resolveUserName(r.UserId),
        Status = r.Status.ToString(),
        Season = r.Season,
        Episode = r.Episode,
        RequestedAt = r.RequestedAt
      })
      .ToList();

    return Ok(result);
  }

  /// <summary>
  /// Live download status (Radarr/Sonarr queue) for every request (administrators only) — lets the
  /// admin requests table show a "Downloading" badge.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The download statuses, keyed by request id.</response>
  /// <returns>The list of live download statuses.</returns>
  [HttpGet("All/DownloadStatus")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<DownloadStatusDto>>> AllDownloadStatus(CancellationToken cancellationToken)
  {
    var items = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var statuses = await _servarrStatus.GetStatusesAsync(items, cancellationToken).ConfigureAwait(false);
    return Ok(statuses);
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
    if (updated is null)
    {
      return NotFound();
    }

    _ = _activityLog.LogAsync("info", "user", _resolveUserName(userId) + " requested deletion of " + updated.Title, _resolveUserName(userId), CancellationToken.None);
    return Ok(updated);
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
    if (updated is null)
    {
      return NotFound();
    }

    _ = _activityLog.LogAsync("info", "user", _resolveUserName(userId) + " cancelled deletion of " + updated.Title, _resolveUserName(userId), CancellationToken.None);
    return Ok(updated);
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
    var existing = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

    // Remove it locally first, so a dispatch or retry that comes after no longer finds it, then withdraw it
    // from the backend in the background: that waits for any backend operation already running for the
    // title (a dispatch still adding the series), so the user is not kept waiting on it. Only an approved
    // or already dispatched request can have reached the backend.
    var cancelled = await _store.CancelAsync(id, userId, cancellationToken).ConfigureAwait(false);
    if (cancelled && existing is not null && (existing.Status == RequestStatus.Approved || existing.DispatchedAt is not null))
    {
      _ = _downloadDispatcher.CancelAsync(existing, CancellationToken.None);
    }

    return cancelled ? NoContent() : NotFound();
  }

  /// <summary>
  /// Re-triggers a release search for one of the current user's approved requests that dispatched but
  /// never became available ("blocked" / not found). Asks the backend to search again (Radarr/Sonarr)
  /// or re-sends the dispatch (webhook/script), clearing the stored error on success.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The request after the retry attempt (with the error cleared or refreshed).</response>
  /// <response code="404">No matching request owned by the user.</response>
  /// <response code="409">The request is not in a state that can be retried.</response>
  /// <returns>The refreshed request, or an error status.</returns>
  [HttpPost("{id}/Retry")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<RequestRecord>> Retry(Guid id, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);

    // Manual retry is admin-only unless the admin has opted users in (Radarr/Sonarr already auto-search).
    var isAdmin = await _userAccessor.IsAdministratorAsync(Request).ConfigureAwait(false);
    if (!isAdmin && !(Plugin.Instance?.Configuration.AllowUserRetrySearch ?? false))
    {
      return Forbid();
    }

    var existing = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    // An admin can retry any request (incl. ones made on behalf of others); a user only their own.
    if (existing is null || (!isAdmin && existing.UserId != userId))
    {
      return NotFound();
    }

    if (existing.Status != RequestStatus.Approved)
    {
      return Conflict("Only an approved request can be retried.");
    }

    await _downloadDispatcher.RetryAsync(existing, cancellationToken).ConfigureAwait(false);
    var updated = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    return Ok(updated);
  }

  /// <summary>
  /// Re-triggers a release search for every approved-but-not-yet-available request in one go
  /// (administrators only): the ones that dispatched but are stuck/blocked/errored. Each retry never
  /// throws; the response reports how many were retried and how many failed.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The bulk retry outcome (retried / failed / total counts).</response>
  /// <returns>The retry counts.</returns>
  [HttpPost("RetryAll")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<RetryAllResultDto>> RetryAll(CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    // Approved = dispatched but not yet available: the set that can be stuck/blocked/errored (mirrors the
    // per-row Retry button, which is shown on exactly these). Each retry clears or refreshes its own error.
    var candidates = all.Where(r => r.Status == RequestStatus.Approved).ToList();

    var retried = 0;
    foreach (var request in candidates)
    {
      if (await _downloadDispatcher.RetryAsync(request, cancellationToken).ConfigureAwait(false))
      {
        retried++;
      }
    }

    return Ok(new RetryAllResultDto
    {
      Retried = retried,
      Failed = candidates.Count - retried,
      Total = candidates.Count,
    });
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
  /// Flags a request's media for deletion (administrators only): the scheduled task purges the download
  /// backend and removes the files after the retention period. Cancellable until then. Unlike
  /// <see cref="Delete"/> (which only removes the request record), this deletes the actual media.
  /// </summary>
  /// <param name="id">The request identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The media was flagged for deletion.</response>
  /// <response code="404">No such request, or its media is not available.</response>
  /// <returns>The updated request, or 404.</returns>
  [HttpPost("{id}/DeleteMedia")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<RequestRecord>> DeleteMedia(Guid id, CancellationToken cancellationToken)
  {
    var updated = await _store.AdminFlagDeletionAsync(id, cancellationToken).ConfigureAwait(false);
    if (updated is null)
    {
      return NotFound();
    }

    var adminId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    _ = _activityLog.LogAsync("info", "admin", _resolveUserName(adminId) + " flagged \"" + updated.Title + "\" for media deletion", _resolveUserName(adminId), CancellationToken.None);
    return Ok(updated);
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
  /// Creates a request on behalf of another user (administrators only). It skips the per-period request
  /// limit, but follows the same quota and duplicate rules as the user's own requests: acting for someone
  /// must not duplicate what they already have or over-commit their quota.
  /// </summary>
  /// <param name="dto">The request payload, including the target user.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created request.</response>
  /// <response code="400">The payload was invalid.</response>
  /// <response code="409">The user already has this title, or it is already on its way.</response>
  /// <response code="422">The request is larger than the user's whole disk quota.</response>
  /// <returns>The persisted request.</returns>
  [HttpPost("ForUser")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
  public async Task<ActionResult<RequestRecord>> CreateForUser([FromBody] AdminCreateRequestDto dto, CancellationToken cancellationToken)
  {
    return ToActionResult(await _creator.CreateOnBehalfAsync(dto, cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Creates a request a parent makes for one of their children: the request is the child's, on the
  /// child's quota and limits, and only a title suited to the child's age goes through.
  /// </summary>
  /// <param name="dto">The request payload, including the child.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created request (approved, or pending approval or quota).</response>
  /// <response code="400">The payload was invalid.</response>
  /// <response code="403">The caller is not a parent of this child, or the title is above the child's age.</response>
  /// <response code="409">The child already has this title, or it is already on its way.</response>
  /// <response code="422">The request is larger than the child's whole disk quota.</response>
  /// <response code="429">The child reached their request limit for the period.</response>
  /// <response code="503">The title's rating could not be checked.</response>
  /// <returns>The persisted request.</returns>
  [HttpPost("ForChild")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status403Forbidden)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
  [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<RequestRecord>> CreateForChild([FromBody] ChildRequestDto dto, CancellationToken cancellationToken)
  {
    if (dto is null)
    {
      return BadRequest("A request is required.");
    }

    var parentId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    return ToActionResult(await _creator.CreateForChildAsync(parentId, dto.ChildId, dto, cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Assigns ownership of a media already in the library to a user (administrators only). For a title the
  /// admin added by hand that nobody requested: it grants the user ownership straight away — an available
  /// request pointing at the library item — and tells them it was added to their library.
  /// </summary>
  /// <param name="dto">The media and the target user.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The ownership record (created or renewed).</response>
  /// <response code="400">The payload was invalid, or the media is not in the library.</response>
  /// <returns>The persisted available request.</returns>
  [HttpPost("AssignOwner")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RequestRecord>> AssignOwner([FromBody] AdminCreateRequestDto dto, CancellationToken cancellationToken)
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

    // Resolve the exact library item for the scope (a season, an episode, or the whole movie/series), so
    // ownership points at what actually exists — assigning is only for media that is already present.
    var itemId = FindOwnableItemId(dto.MediaType, dto.TmdbId, dto.Season, dto.Episode);
    if (string.IsNullOrEmpty(itemId))
    {
      return BadRequest("This title is not available in the library.");
    }

    // Already owned by this user (this scope, or a broader one covering it) → renew rather than duplicate.
    using var creation = await _creationGate.EnterAsync(dto.UserId, cancellationToken).ConfigureAwait(false);
    var theirs = await _store.GetByUserAsync(dto.UserId, cancellationToken).ConfigureAwait(false);
    var owned = theirs.FirstOrDefault(r =>
      r.TmdbId == dto.TmdbId
      && string.Equals(r.MediaType, dto.MediaType, StringComparison.Ordinal)
      && r.Status == RequestStatus.Available
      && MediaScope.Overlaps(dto.Season, dto.Episode, r.Season, r.Episode));
    if (owned is not null)
    {
      var renewed = await _store.RenewAvailableAsync(owned.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
      return Ok(renewed);
    }

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
        Status = RequestStatus.Available,
        JellyfinItemId = itemId,
        AvailableAt = DateTime.UtcNow
      },
      cancellationToken).ConfigureAwait(false);

    var t = ServerStrings.For(Plugin.Instance?.Configuration?.Language);
    var title = NotificationMessages.TitleOf(created, t);
    _ = _notificationService.NotifyPersonalAsync(
      dto.UserId,
      PersonalNotifyKind.None,
      created.Title,
      t("notif_assigned_subject"),
      t("notif_assigned_body").Replace("{title}", title, StringComparison.Ordinal),
      created.PosterPath,
      CancellationToken.None);

    var admin = _resolveUserName(await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false));
    _ = _activityLog.LogAsync("info", "user", admin + " assigned " + created.Title + " to " + _resolveUserName(dto.UserId), admin, CancellationToken.None);
    return Ok(created);
  }

  private static bool IsValidMediaType(string mediaType)
    => string.Equals(mediaType, "movie", StringComparison.Ordinal)
       || string.Equals(mediaType, "tv", StringComparison.Ordinal);

  // Refuses a title above the user's parental restriction (Jellyfin parental control or child group).
  // The catalog already hides such titles; this is what stops a hand-made call. A rating that cannot be
  // looked up is a refusal too — a restricted user is never let through on a guess.
  private async Task<ActionResult?> CheckParentalRestrictionAsync(Guid userId, string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    var restriction = _restrictions.For(userId);
    if (!restriction.IsRestricted)
    {
      return null;
    }

    try
    {
      return await _restrictions.IsAllowedAsync(restriction, mediaType, tmdbId, cancellationToken).ConfigureAwait(false)
        ? null
        : StatusCode(StatusCodes.Status403Forbidden, "This title is above your parental rating limit.");
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
      || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, "The title's rating cannot be checked right now.");
    }
  }

  // The HTTP answer for a creation attempt: the recorded request, or the status code of its refusal.
  private ActionResult<RequestRecord> ToActionResult(RequestCreationResult result) => result.Outcome switch
  {
    RequestCreationOutcome.Created => Ok(result.Request),
    RequestCreationOutcome.Invalid => BadRequest(result.Message),
    RequestCreationOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden, result.Message),
    RequestCreationOutcome.AlreadyCovered => Conflict(result.Message),
    RequestCreationOutcome.TooLarge => UnprocessableEntity(result.Message),
    RequestCreationOutcome.RateLimited => StatusCode(StatusCodes.Status429TooManyRequests, result.Message),
    _ => StatusCode(StatusCodes.Status503ServiceUnavailable, result.Message),
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
