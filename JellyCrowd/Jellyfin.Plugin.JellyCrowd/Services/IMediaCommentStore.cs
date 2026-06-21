using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Stores user comments on catalog titles. Bounded per title so the store stays small.
/// </summary>
public interface IMediaCommentStore
{
  /// <summary>
  /// Adds a comment (assigning id/timestamp) and trims the title's comments to the cap.
  /// </summary>
  /// <param name="comment">The comment to add.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The stored comment.</returns>
  Task<MediaComment> AddAsync(MediaComment comment, CancellationToken cancellationToken);

  /// <summary>
  /// Adds the user's review for a title, or updates it if they already reviewed that title (one review
  /// per user per title). The existing id is preserved; rating/text/timestamp are refreshed.
  /// </summary>
  /// <param name="review">The review to add or update (matched by user id + title).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The stored review.</returns>
  Task<MediaComment> AddOrUpdateAsync(MediaComment review, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the comments for a title, newest first.
  /// </summary>
  /// <param name="mediaType">The media type.</param>
  /// <param name="tmdbId">The TMDB id.</param>
  /// <param name="includeHidden">Whether to include admin-hidden comments.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The comments.</returns>
  Task<IReadOnlyList<MediaComment>> GetForTitleAsync(string mediaType, int tmdbId, bool includeHidden, CancellationToken cancellationToken);

  /// <summary>
  /// Sets (or clears) the hidden flag on a comment (admin moderation).
  /// </summary>
  /// <param name="id">The comment id.</param>
  /// <param name="hidden">Whether the comment should be hidden.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated comment, or <c>null</c> if not found.</returns>
  Task<MediaComment?> SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken);

  /// <summary>
  /// Deletes a comment (admin moderation, or the author removing their own).
  /// </summary>
  /// <param name="id">The comment id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when a comment was removed.</returns>
  Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

  /// <summary>
  /// Gets a comment by id (used to authorize an author-initiated delete).
  /// </summary>
  /// <param name="id">The comment id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The comment, or <c>null</c>.</returns>
  Task<MediaComment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
