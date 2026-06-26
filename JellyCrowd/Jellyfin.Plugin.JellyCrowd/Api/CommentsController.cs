using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
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
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="CommentsController"/> class.
  /// </summary>
  /// <param name="store">The comment store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="resolveUserName">Resolves a user id to a display name.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  public CommentsController(IMediaCommentStore store, ICurrentUserAccessor userAccessor, Func<Guid, string> resolveUserName, Func<PluginConfiguration> config)
  {
    _store = store;
    _userAccessor = userAccessor;
    _resolveUserName = resolveUserName;
    _config = config;
  }

  /// <summary>
  /// Gets the visible comments for a title, newest first.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The title's reviews + internal average.</response>
  /// <returns>The internal average, count and anonymised reviews.</returns>
  [HttpGet("{mediaType}/{tmdbId:int}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<MediaReviewsDto>> Get(string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    var dto = new MediaReviewsDto();
    if (!_config().CommentsEnabled)
    {
      return Ok(dto);
    }

    var reviews = await _store.GetForTitleAsync(mediaType, tmdbId, includeHidden: false, cancellationToken).ConfigureAwait(false);
    var rated = reviews.Where(r => r.Rating > 0).ToList();
    if (rated.Count > 0)
    {
      dto.Average = Math.Round(rated.Average(r => r.Rating), 1);
      dto.Count = rated.Count;
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var isAdmin = await _userAccessor.IsAdministratorAsync(Request).ConfigureAwait(false);
    var showAuthors = isAdmin || _config().ShowReviewAuthors;
    foreach (var r in reviews)
    {
      dto.Reviews.Add(new ReviewView
      {
        Id = r.Id,
        Rating = r.Rating,
        Text = r.Text,
        CreatedAt = r.CreatedAt,
        Mine = r.UserId == userId,
        // Anonymous to non-admins unless the admin opted to show author names (admins always see them).
        UserName = showAuthors ? r.UserName : null
      });
    }

    return Ok(dto);
  }

  /// <summary>
  /// Posts (or updates) the caller's review of a title: a 1–10 rating with optional text. One review
  /// per user per title.
  /// </summary>
  /// <param name="dto">The review payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created/updated review.</response>
  /// <response code="400">The payload was invalid.</response>
  /// <returns>The persisted review.</returns>
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<MediaComment>> Post([FromBody] CommentDto dto, CancellationToken cancellationToken)
  {
    if (!_config().CommentsEnabled)
    {
      return StatusCode(StatusCodes.Status403Forbidden, "Reviews are disabled.");
    }

    if (dto is null)
    {
      return BadRequest("A review payload is required.");
    }

    if (dto.Rating < 1 || dto.Rating > 10)
    {
      return BadRequest("A rating from 1 to 10 is required.");
    }

    if (!string.Equals(dto.MediaType, "movie", StringComparison.Ordinal) && !string.Equals(dto.MediaType, "tv", StringComparison.Ordinal))
    {
      return BadRequest("The 'mediaType' must be 'movie' or 'tv'.");
    }

    var text = (dto.Text ?? string.Empty).Trim();
    if (text.Length > MaxCommentLength)
    {
      text = text[..MaxCommentLength];
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var saved = await _store.AddOrUpdateAsync(
      new MediaComment
      {
        MediaType = dto.MediaType,
        TmdbId = dto.TmdbId,
        UserId = userId,
        UserName = _resolveUserName(userId),
        Text = text,
        Rating = dto.Rating
      },
      cancellationToken).ConfigureAwait(false);

    return Ok(saved);
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
  /// Gets every review across all titles for the admin Moderation page, newest first (administrators only).
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">All reviews, including hidden ones, with author names.</response>
  /// <returns>The full list of reviews.</returns>
  [HttpGet("All")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<ModeratedReviewDto>>> GetAll(CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var result = all.Select(r => new ModeratedReviewDto
    {
      Id = r.Id,
      MediaType = r.MediaType,
      TmdbId = r.TmdbId,
      UserName = r.UserName,
      Rating = r.Rating,
      Text = r.Text,
      CreatedAt = r.CreatedAt,
      Hidden = r.Hidden
    }).ToList();

    return Ok(result);
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
  /// Restores a hidden comment to the public list (administrators only).
  /// </summary>
  /// <param name="id">The comment id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The comment was un-hidden.</response>
  /// <response code="404">No such comment.</response>
  /// <returns>No content on success; 404 otherwise.</returns>
  [HttpPost("{id:guid}/Show")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Show(Guid id, CancellationToken cancellationToken)
  {
    var updated = await _store.SetHiddenAsync(id, hidden: false, cancellationToken).ConfigureAwait(false);
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
