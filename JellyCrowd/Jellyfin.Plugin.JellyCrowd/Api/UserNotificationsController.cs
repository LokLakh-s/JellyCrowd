using System;
using System.Linq;
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
/// The current user's in-app notification feed (header bell): list, mark read, and clear.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/Notifications")]
[Produces(MediaTypeNames.Application.Json)]
public class UserNotificationsController : ControllerBase
{
  private readonly IUserNotificationStore _store;
  private readonly IUserPrefsStore _prefs;
  private readonly ICurrentUserAccessor _userAccessor;

  /// <summary>
  /// Initializes a new instance of the <see cref="UserNotificationsController"/> class.
  /// </summary>
  /// <param name="store">The per-user notification store.</param>
  /// <param name="prefs">The per-user delivery preferences store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  public UserNotificationsController(IUserNotificationStore store, IUserPrefsStore prefs, ICurrentUserAccessor userAccessor)
  {
    _store = store;
    _prefs = prefs;
    _userAccessor = userAccessor;
  }

  /// <summary>
  /// Gets the current user's notifications and unread count.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The notifications and unread count.</response>
  /// <returns>The notification feed.</returns>
  [HttpGet("Mine")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<UserNotificationsDto>> Mine(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var items = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    return Ok(new UserNotificationsDto { Items = items, Unread = items.Count(n => !n.Read) });
  }

  /// <summary>
  /// Marks the current user's notifications as read (all of them).
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The notifications were marked read.</response>
  /// <returns>No content.</returns>
  [HttpPost("Mine/Read")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    await _store.MarkReadAsync(userId, null, cancellationToken).ConfigureAwait(false);
    return NoContent();
  }

  /// <summary>
  /// Clears all of the current user's notifications.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The notifications were cleared.</response>
  /// <returns>No content.</returns>
  [HttpPost("Mine/Clear")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public async Task<IActionResult> ClearAll(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    await _store.ClearAsync(userId, null, cancellationToken).ConfigureAwait(false);
    return NoContent();
  }

  /// <summary>
  /// Gets the current user's personal delivery preferences (email / ntfy).
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The preferences.</response>
  /// <returns>The caller's notification preferences.</returns>
  [HttpGet("Mine/Prefs")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<UserNotificationPrefs>> GetPrefs(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    return Ok(await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Saves the current user's personal delivery preferences.
  /// </summary>
  /// <param name="dto">The preferences (the user id is taken from the caller, not the body).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The saved preferences.</response>
  /// <returns>The persisted notification preferences.</returns>
  [HttpPost("Mine/Prefs")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<UserNotificationPrefs>> SetPrefs([FromBody] UserNotificationPrefs dto, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var saved = await _prefs.SetAsync(
      new UserNotificationPrefs
      {
        UserId = userId,
        Enabled = dto?.Enabled ?? true,
        Email = dto?.Email,
        NtfyTopic = dto?.NtfyTopic
      },
      cancellationToken).ConfigureAwait(false);
    return Ok(saved);
  }

  /// <summary>
  /// Clears one of the current user's notifications.
  /// </summary>
  /// <param name="id">The notification id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The notification was cleared (or did not exist).</response>
  /// <returns>No content.</returns>
  [HttpPost("Mine/Clear/{id}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public async Task<IActionResult> ClearOne(Guid id, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    await _store.ClearAsync(userId, id, cancellationToken).ConfigureAwait(false);
    return NoContent();
  }
}
