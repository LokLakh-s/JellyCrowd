using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using JfEpisode = MediaBrowser.Controller.Entities.TV.Episode;
using JfSeries = MediaBrowser.Controller.Entities.TV.Series;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Removes movies and shows from the caller's "Continue watching" and "Next up" rows, on every client,
/// without touching their playback position; and lists or restores what was removed.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/ContinueWatching")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class ContinueWatchingController : ControllerBase
{
  private readonly IHiddenResumeStore _store;
  private readonly ILibraryManager _libraryManager;
  private readonly ICurrentUserAccessor _userAccessor;

  /// <summary>
  /// Initializes a new instance of the <see cref="ContinueWatchingController"/> class.
  /// </summary>
  /// <param name="store">The removals store.</param>
  /// <param name="libraryManager">The library manager (resolves the item being removed).</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  public ContinueWatchingController(IHiddenResumeStore store, ILibraryManager libraryManager, ICurrentUserAccessor userAccessor)
  {
    _store = store;
    _libraryManager = libraryManager;
    _userAccessor = userAccessor;
  }

  /// <summary>
  /// Lists what the caller removed, newest first.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The removals.</response>
  /// <returns>The caller's removals.</returns>
  [HttpGet("Hidden")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<HiddenResumeEntry>>> Hidden(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    return Ok(await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Removes a movie, or an episode's whole show, from the caller's "Continue watching" and "Next up".
  /// </summary>
  /// <param name="itemId">The movie, episode or show.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">Removed.</response>
  /// <response code="404">No such item.</response>
  /// <returns>No content.</returns>
  [HttpPost("Hide/{itemId:guid}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Hide(Guid itemId, CancellationToken cancellationToken)
  {
    var item = _libraryManager.GetItemById(itemId);
    if (item is null)
    {
      return NotFound();
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    Guid? seriesId = item switch
    {
      JfEpisode episode when episode.SeriesId != Guid.Empty => episode.SeriesId,
      JfSeries series => series.Id,
      _ => null
    };
    var title = item is JfEpisode ep && !string.IsNullOrEmpty(ep.SeriesName) ? ep.SeriesName : item.Name;
    await _store.HideAsync(
      new HiddenResumeEntry { UserId = userId, ItemId = itemId, SeriesId = seriesId, Title = title ?? string.Empty, HiddenAtUtc = DateTime.UtcNow },
      cancellationToken).ConfigureAwait(false);
    return NoContent();
  }

  /// <summary>
  /// Puts a removed movie or show back.
  /// </summary>
  /// <param name="itemId">The movie, episode or show that was removed.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">Put back.</response>
  /// <response code="404">Nothing of the kind was removed.</response>
  /// <returns>No content.</returns>
  [HttpPost("Unhide/{itemId:guid}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Unhide(Guid itemId, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    return await _store.UnhideAsync(userId, itemId, cancellationToken).ConfigureAwait(false) ? NoContent() : NotFound();
  }
}
