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
/// Admin library-cleanup: lists library movies/shows with their Jelly Crowd owner count so the admin
/// can spot and delete orphan media (owned by nobody). Administrators only.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("JellyCrowd/Maintenance")]
[Produces(MediaTypeNames.Application.Json)]
public class LibraryMaintenanceController : ControllerBase
{
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly IRequestStore _store;
  private readonly IMediaDeleter _mediaDeleter;

  /// <summary>
  /// Initializes a new instance of the <see cref="LibraryMaintenanceController"/> class.
  /// </summary>
  /// <param name="libraryMatcher">The library matcher (enumerates library media).</param>
  /// <param name="store">The request store (owner counts).</param>
  /// <param name="mediaDeleter">The media deleter.</param>
  public LibraryMaintenanceController(ILibraryMatcher libraryMatcher, IRequestStore store, IMediaDeleter mediaDeleter)
  {
    _libraryMatcher = libraryMatcher;
    _store = store;
    _mediaDeleter = mediaDeleter;
  }

  /// <summary>
  /// Lists library media with their owner count (active "available" requests), orphans first.
  /// </summary>
  /// <param name="orphansOnly">When <c>true</c>, only return media owned by nobody.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The library media.</response>
  /// <returns>The library media with owner counts.</returns>
  [HttpGet("Media")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<LibraryMediaItem>>> GetMedia([FromQuery] bool orphansOnly, CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);

    // Ownership persists until a deletion actually completes (not the moment it is requested), so a title
    // still within its deletion grace period is not an orphan yet — count those owners too, matching the
    // user-facing "My library" count. The count drops only when the scheduled deletion removes the request.
    var owners = all
      .Where(r => r.Status == RequestStatus.Available)
      .ToList();

    var media = _libraryMatcher.ListLibraryMedia();
    var result = new List<LibraryMediaItem>(media.Count);
    foreach (var item in media)
    {
      // Count only the requests whose scope overlaps this entry — a per-season entry is owned by a
      // whole-series request or a request for that same season, not by a request for a different season.
      item.OwnerCount = owners.Count(r =>
        r.TmdbId == item.TmdbId
        && string.Equals(r.MediaType, item.MediaType, StringComparison.Ordinal)
        && MediaScope.Overlaps(item.Season, null, r.Season, r.Episode));
      if (!orphansOnly || item.OwnerCount == 0)
      {
        result.Add(item);
      }
    }

    // Orphans first, then largest first (best cleanup candidates on top).
    return Ok(result.OrderBy(i => i.OwnerCount).ThenByDescending(i => i.SizeBytes).ToList());
  }

  /// <summary>
  /// Deletes a library item from disk (and removes any Jelly Crowd requests referencing it).
  /// </summary>
  /// <param name="itemId">The Jellyfin library item id (32-char hex).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The item was deleted.</response>
  /// <response code="404">The item could not be deleted.</response>
  /// <returns>No content on success.</returns>
  [HttpPost("Media/{itemId}/Delete")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> DeleteMedia(string itemId, CancellationToken cancellationToken)
  {
    var deleted = _mediaDeleter.Delete(itemId);

    // Clean up any plugin requests that pointed at this item so nothing dangles.
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    foreach (var request in all.Where(r => string.Equals(r.JellyfinItemId, itemId, StringComparison.Ordinal)))
    {
      await _store.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
    }

    return deleted ? NoContent() : NotFound();
  }
}
