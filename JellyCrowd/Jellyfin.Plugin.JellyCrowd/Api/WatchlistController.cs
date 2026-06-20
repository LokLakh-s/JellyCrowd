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
/// The current user's watchlist (followed titles).
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/Watchlist")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class WatchlistController : ControllerBase
{
  private readonly IWatchlistStore _store;
  private readonly ICurrentUserAccessor _userAccessor;

  /// <summary>
  /// Initializes a new instance of the <see cref="WatchlistController"/> class.
  /// </summary>
  /// <param name="store">The watchlist store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  public WatchlistController(IWatchlistStore store, ICurrentUserAccessor userAccessor)
  {
    _store = store;
    _userAccessor = userAccessor;
  }

  /// <summary>
  /// Lists the current user's watchlist.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The user's watchlist.</response>
  /// <returns>The watchlist entries, newest first.</returns>
  [HttpGet]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<WatchlistEntry>>> Mine(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    return Ok(await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Adds a title to the current user's watchlist.
  /// </summary>
  /// <param name="dto">The title to add.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The stored entry.</response>
  /// <response code="400">The payload was invalid.</response>
  /// <returns>The watchlist entry.</returns>
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<WatchlistEntry>> Add([FromBody] WatchlistItemDto dto, CancellationToken cancellationToken)
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
    var entry = await _store.AddAsync(
      new WatchlistEntry
      {
        UserId = userId,
        TmdbId = dto.TmdbId,
        MediaType = dto.MediaType,
        Title = dto.Title,
        PosterPath = dto.PosterPath,
        ReleaseDate = dto.ReleaseDate
      },
      cancellationToken).ConfigureAwait(false);
    return Ok(entry);
  }

  /// <summary>
  /// Removes a title from the current user's watchlist.
  /// </summary>
  /// <param name="dto">The title to remove (TMDB id + media type).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">Removed (or not present).</response>
  /// <response code="400">The payload was invalid.</response>
  /// <returns>No content.</returns>
  [HttpPost("Remove")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Remove([FromBody] WatchlistItemDto dto, CancellationToken cancellationToken)
  {
    if (dto is null || !IsValidMediaType(dto.MediaType))
    {
      return BadRequest("The 'mediaType' must be 'movie' or 'tv'.");
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    await _store.RemoveAsync(userId, dto.TmdbId, dto.MediaType, cancellationToken).ConfigureAwait(false);
    return NoContent();
  }

  private static bool IsValidMediaType(string mediaType)
    => string.Equals(mediaType, "movie", StringComparison.Ordinal)
       || string.Equals(mediaType, "tv", StringComparison.Ordinal);
}
