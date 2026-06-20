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
/// User comments on catalog titles, plus admin moderation (hide / delete).
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/Comments")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class CommentsController : ControllerBase
{
  private const int MaxCommentLength = 2000;

  private readonly IMediaCommentStore _store;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly Func<Guid, string> _resolveUserName;

  /// <summary>
  /// Initializes a new instance of the <see cref="CommentsController"/> class.
  /// </summary>
  /// <param name="store">The comment store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="resolveUserName">Resolves a user id to a display name.</param>
  public CommentsController(IMediaCommentStore store, ICurrentUserAccessor userAccessor, Func<Guid, string> resolveUserName)
  {
    _store = store;
    _userAccessor = userAccessor;
    _resolveUserName = resolveUserName;
  }

  /// <summary>
  /// Gets the visible comments for a title, newest first.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The comments.</response>
  /// <returns>The visible comments.</returns>
  [HttpGet("{mediaType}/{tmdbId:int}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<MediaComment>>> Get(string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    return Ok(await _store.GetForTitleAsync(mediaType, tmdbId, includeHidden: false, cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Posts a comment on a title.
  /// </summary>
  /// <param name="dto">The comment payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created comment.</response>
  /// <response code="400">The payload was invalid.</response>
  /// <returns>The persisted comment with its id and timestamp.</returns>
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<MediaComment>> Post([FromBody] CommentDto dto, CancellationToken cancellationToken)
  {
    if (dto is null || string.IsNullOrWhiteSpace(dto.Text))
    {
      return BadRequest("A comment text is required.");
    }

    if (!string.Equals(dto.MediaType, "movie", StringComparison.Ordinal) && !string.Equals(dto.MediaType, "tv", StringComparison.Ordinal))
    {
      return BadRequest("The 'mediaType' must be 'movie' or 'tv'.");
    }

    var text = dto.Text.Trim();
    if (text.Length > MaxCommentLength)
    {
      text = text[..MaxCommentLength];
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var created = await _store.AddAsync(
      new MediaComment
      {
        MediaType = dto.MediaType,
        TmdbId = dto.TmdbId,
        UserId = userId,
        UserName = _resolveUserName(userId),
        Text = text
      },
      cancellationToken).ConfigureAwait(false);

    return Ok(created);
  }

  /// <summary>
  /// Deletes one of the caller's own comments.
  /// </summary>
  /// <param name="id">The comment id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The comment was deleted.</response>
  /// <response code="404">No matching comment owned by the caller.</response>
  /// <returns>No content on success; 404 otherwise.</returns>
  [HttpPost("{id:guid}/DeleteMine")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> DeleteMine(Guid id, CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var existing = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    if (existing is null || existing.UserId != userId)
    {
      return NotFound();
    }

    await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    return NoContent();
  }

  /// <summary>
  /// Hides a comment from the public list (administrators only).
  /// </summary>
  /// <param name="id">The comment id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The comment was hidden.</response>
  /// <response code="404">No such comment.</response>
  /// <returns>No content on success; 404 otherwise.</returns>
  [HttpPost("{id:guid}/Hide")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Hide(Guid id, CancellationToken cancellationToken)
  {
    var updated = await _store.SetHiddenAsync(id, hidden: true, cancellationToken).ConfigureAwait(false);
    return updated is null ? NotFound() : NoContent();
  }

  /// <summary>
  /// Deletes any comment (administrators only).
  /// </summary>
  /// <param name="id">The comment id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The comment was deleted.</response>
  /// <response code="404">No such comment.</response>
  /// <returns>No content on success; 404 otherwise.</returns>
  [HttpPost("{id:guid}/Delete")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
  {
    return await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false) ? NoContent() : NotFound();
  }
}
