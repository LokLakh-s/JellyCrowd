using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Stores what each user removed from their "Continue watching" and "Next up" rows.
/// </summary>
public interface IHiddenResumeStore
{
  /// <summary>
  /// Gets what a user removed, newest first.
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The user's entries.</returns>
  Task<IReadOnlyList<HiddenResumeEntry>> GetByUserAsync(Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Records a removal; removing the same movie or show again refreshes it instead of duplicating.
  /// </summary>
  /// <param name="entry">The entry.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes once saved.</returns>
  Task HideAsync(HiddenResumeEntry entry, CancellationToken cancellationToken);

  /// <summary>
  /// Puts back what a removal took away: the entry for this item, or for the show it belongs to.
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="itemId">The movie, the episode, or the show.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when an entry was removed.</returns>
  Task<bool> UnhideAsync(Guid userId, Guid itemId, CancellationToken cancellationToken);

  /// <summary>
  /// Puts back what a user starts playing again: the movie itself, or any episode's show.
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="itemId">The item being played.</param>
  /// <param name="seriesId">Its show, for an episode.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The number of entries removed.</returns>
  Task<int> UnhideForPlaybackAsync(Guid userId, Guid itemId, Guid? seriesId, CancellationToken cancellationToken);
}
