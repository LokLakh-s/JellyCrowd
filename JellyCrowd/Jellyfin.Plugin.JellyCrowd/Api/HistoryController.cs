using System;
using System.Collections.Generic;
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
/// The current user's personal viewing history: list it, hide/show it, and erase it. The history is
/// never removed automatically — only the user clears it here. Hiding it does not stop recording (the
/// admin statistics stay complete); it only turns off the personal view.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/History")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class HistoryController : ControllerBase
{
  // The most entries the personal view returns in one call. Years of history stay on the server (nothing
  // is auto-erased); this only bounds one response so the page is not asked to render an unbounded list.
  private const int MaxReturned = 1000;

  private readonly IPlaybackHistoryStore _history;
  private readonly IUserPrefsStore _prefs;
  private readonly ICurrentUserAccessor _userAccessor;

  /// <summary>
  /// Initializes a new instance of the <see cref="HistoryController"/> class.
  /// </summary>
  /// <param name="history">The playback history store.</param>
  /// <param name="prefs">The per-user preferences store (holds the hidden flag).</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  public HistoryController(IPlaybackHistoryStore history, IUserPrefsStore prefs, ICurrentUserAccessor userAccessor)
  {
    _history = history;
    _prefs = prefs;
    _userAccessor = userAccessor;
  }

  /// <summary>
  /// Gets the caller's own viewing history, newest first.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The history (empty when the user has hidden it).</response>
  /// <returns>The caller's history and whether it is hidden.</returns>
  [HttpGet("Mine")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<MyHistoryDto>> Mine(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var prefs = await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false);
    if (prefs.HistoryHidden)
    {
      return Ok(new MyHistoryDto { Hidden = true });
    }

    var records = await _history.GetByUserAsync(userId, MaxReturned, cancellationToken).ConfigureAwait(false);
    return Ok(new MyHistoryDto { Hidden = false, Entries = records.Select(ToDto).ToList() });
  }

  /// <summary>
  /// Hides or shows the caller's own history (recording is unaffected).
  /// </summary>
  /// <param name="hidden">Whether the history should be hidden from the user.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The preference was saved.</response>
  /// <returns>No content.</returns>
  [HttpPost("Mine/Hidden")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public async Task<IActionResult> SetHidden([FromQuery] bool hidden, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    // Read-modify-write so the notification preferences on the same record are preserved.
    var prefs = await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false);
    prefs.UserId = userId;
    prefs.HistoryHidden = hidden;
    await _prefs.SetAsync(prefs, cancellationToken).ConfigureAwait(false);
    return NoContent();
  }

  /// <summary>
  /// Erases the caller's entire history.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The history was erased; returns how many entries were removed.</response>
  /// <returns>The number of entries removed.</returns>
  [HttpPost("Mine/Clear")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<int>> Clear(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var removed = await _history.DeleteByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    return Ok(removed);
  }

  /// <summary>
  /// Deletes a single entry from the caller's history.
  /// </summary>
  /// <param name="id">The entry id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The entry was removed.</response>
  /// <response code="404">No such entry belongs to the caller.</response>
  /// <returns>No content, or 404.</returns>
  [HttpPost("Mine/{id:guid}/Delete")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> DeleteOne(Guid id, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var removed = await _history.DeleteOneAsync(userId, id, cancellationToken).ConfigureAwait(false);
    return removed ? NoContent() : NotFound();
  }

  private static PlaybackHistoryEntryDto ToDto(PlaybackRecord r) => new()
  {
    Id = r.Id,
    Title = r.ItemName,
    ItemType = r.ItemType,
    SeriesName = r.SeriesName,
    Season = r.Season,
    Episode = r.Episode,
    LibraryName = r.LibraryName,
    PlayedAtUtc = r.PlayedAtUtc,
    Minutes = r.Minutes
  };
}
