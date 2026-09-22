using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Stores the admin-authored polls and the votes cast in them. Bounded (most-recent cap, closed polls
/// dropped before open ones) so the store stays small.
/// </summary>
public interface IPollStore
{
  /// <summary>
  /// Gets every poll, newest first.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The polls.</returns>
  Task<IReadOnlyList<Poll>> GetAllAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Gets a poll by id.
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The poll, or <c>null</c> when there is none.</returns>
  Task<Poll?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

  /// <summary>
  /// Adds a poll (assigning its id and creation time) and trims the store to the cap.
  /// </summary>
  /// <param name="poll">The poll to add.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The stored poll.</returns>
  Task<Poll> AddAsync(Poll poll, CancellationToken cancellationToken);

  /// <summary>
  /// Replaces a poll's question, options and settings. Refused once a vote has been cast: rewriting the
  /// options under the voters would change what they answered.
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="updated">The new content (its id and creation time are ignored).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated poll, <c>null</c> when there is no such poll, or the unchanged poll when it already has votes.</returns>
  Task<Poll?> UpdateAsync(Guid id, Poll updated, CancellationToken cancellationToken);

  /// <summary>
  /// Opens or closes a poll by hand.
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="closed">Whether the poll should be closed.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated poll, or <c>null</c> when there is no such poll.</returns>
  Task<Poll?> SetClosedAsync(Guid id, bool closed, CancellationToken cancellationToken);

  /// <summary>
  /// Records a user's vote, replacing the one they cast before.
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="vote">The vote to record.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated poll, or <c>null</c> when there is no such poll.</returns>
  Task<Poll?> VoteAsync(Guid id, PollVote vote, CancellationToken cancellationToken);

  /// <summary>
  /// Deletes a poll and the votes cast in it.
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when a poll was removed.</returns>
  Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
