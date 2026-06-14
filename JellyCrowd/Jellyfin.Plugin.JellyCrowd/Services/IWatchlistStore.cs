using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Persistence for user watchlists (followed titles).
/// </summary>
public interface IWatchlistStore
{
  /// <summary>
  /// Adds an entry for the user (no-op returning the existing one if already present).
  /// </summary>
  /// <param name="entry">The entry to add.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The stored entry.</returns>
  Task<WatchlistEntry> AddAsync(WatchlistEntry entry, CancellationToken cancellationToken);

  /// <summary>
  /// Removes the user's entry for a title.
  /// </summary>
  /// <param name="userId">The owning user.</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <param name="mediaType">The media type.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when an entry was removed.</returns>
  Task<bool> RemoveAsync(Guid userId, int tmdbId, string mediaType, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the user's watchlist, newest first.
  /// </summary>
  /// <param name="userId">The owning user.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The user's entries.</returns>
  Task<IReadOnlyList<WatchlistEntry>> GetByUserAsync(Guid userId, CancellationToken cancellationToken);
}
