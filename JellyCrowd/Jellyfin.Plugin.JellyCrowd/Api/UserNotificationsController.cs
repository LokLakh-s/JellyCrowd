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
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class UserNotificationsController : ControllerBase
{
  private readonly IUserNotificationStore _store;
  private readonly IUserPrefsStore _prefs;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly IActivityLog _activityLog;
  private readonly INotificationService _notifications;
  private readonly Func<Guid, string> _resolveUserName;

  /// <summary>
  /// Initializes a new instance of the <see cref="UserNotificationsController"/> class.
  /// </summary>
  /// <param name="store">The per-user notification store.</param>
  /// <param name="prefs">The per-user delivery preferences store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="activityLog">The activity log (records preference changes).</param>
  /// <param name="notifications">The notification service (delivers the personal test).</param>
  /// <param name="resolveUserName">Resolves a user id to a display name for log messages.</param>
  public UserNotificationsController(IUserNotificationStore store, IUserPrefsStore prefs, ICurrentUserAccessor userAccessor, IActivityLog activityLog, INotificationService notifications, Func<Guid, string> resolveUserName)
  {
    _store = store;
    _prefs = prefs;
    _userAccessor = userAccessor;
    _activityLog = activityLog;
    _notifications = notifications;
    _resolveUserName = resolveUserName;
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

    // A blank e-mail simply disables e-mail delivery; a non-blank one must be a real address before we
    // ever use it as an SMTP recipient (it should not be possible to point delivery at arbitrary text).
    var email = string.IsNullOrWhiteSpace(dto?.Email) ? null : dto!.Email!.Trim();
    if (email is not null && !IsValidEmail(email))
    {
      return BadRequest("Enter a valid e-mail address, or leave it blank to turn e-mail off.");
    }

    // The history-hidden flag lives on the same record but is owned by the history screen, not this form,
    // so carry the stored value over rather than resetting it.
    var existing = await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false);
    var saved = await _prefs.SetAsync(
      new UserNotificationPrefs
      {
        UserId = userId,
        Enabled = dto?.Enabled ?? true,
        Email = email,
        NtfyTopic = dto?.NtfyTopic,
        NotifyAvailableUnreleased = dto?.NotifyAvailableUnreleased ?? false,
        NotifyAvailableReleased = dto?.NotifyAvailableReleased ?? false,
        NotifyDecisions = dto?.NotifyDecisions ?? false,
        NotifyQuotaExpiry = dto?.NotifyQuotaExpiry ?? false,
        HistoryHidden = existing.HistoryHidden
      },
      cancellationToken).ConfigureAwait(false);
    _ = _activityLog.LogAsync("info", "user", _resolveUserName(userId) + " updated their notification preferences", _resolveUserName(userId), CancellationToken.None);
    return Ok(saved);
  }

  /// <summary>
  /// Sends a test notification to the caller's own channels, so they can check that the address they
  /// saved actually reaches them without waiting for a real request to change state.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The test was delivered.</response>
  /// <response code="400">Nothing is configured to deliver to, or delivery failed (details in the response).</response>
  /// <returns>No content on success; a problem with the error otherwise.</returns>
  [HttpPost("Mine/Test")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> TestMine(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    try
    {
      await _notifications.SendPersonalTestAsync(userId, cancellationToken).ConfigureAwait(false);
      return NoContent();
    }
#pragma warning disable CA1031 // Surface any delivery error back to the user who asked for the test.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      // SMTP failures wrap the real cause (cert / auth / connection) in inner exceptions — include them.
      var detail = ex.Message;
      for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
      {
        detail += " → " + inner.Message;
      }

      return Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
    }
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

  // A pragmatic address check: a single "@" with non-empty, dot-bearing, whitespace-free parts. Enough
  // to keep free text out of the SMTP "To" field without pretending to fully validate RFC 5322.
  private static bool IsValidEmail(string email)
  {
    var at = email.IndexOf('@', StringComparison.Ordinal);
    if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1)
    {
      return false;
    }

    var local = email[..at];
    var domain = email[(at + 1)..];
    return !email.Any(char.IsWhiteSpace)
      && local.Length > 0
      && domain.Contains('.', StringComparison.Ordinal)
      && !domain.StartsWith('.') && !domain.EndsWith('.');
  }
}
