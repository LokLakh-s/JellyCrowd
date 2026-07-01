using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Persistence for captured playback history (the data behind the statistics screens).
/// </summary>
public interface IPlaybackHistoryStore
{
  /// <summary>
  /// Appends a completed viewing record (assigns its id).
  /// </summary>
  /// <param name="record">The record to store.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the record is stored.</returns>
  Task AddAsync(PlaybackRecord record, CancellationToken cancellationToken);

  /// <summary>
  /// Appends many records at once (assigning ids), persisting a single time. Used for bulk imports.
  /// </summary>
  /// <param name="records">The records to store.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The number of records kept after the retention bound is applied.</returns>
  Task<int> AddRangeAsync(IEnumerable<PlaybackRecord> records, CancellationToken cancellationToken);

  /// <summary>
  /// Gets every stored record, newest first.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>All records.</returns>
  Task<IReadOnlyList<PlaybackRecord>> GetAllAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Gets the records played at or after the given UTC instant, newest first.
  /// </summary>
  /// <param name="sinceUtc">The window start (UTC).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The records in the window.</returns>
  Task<IReadOnlyList<PlaybackRecord>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken);
}
