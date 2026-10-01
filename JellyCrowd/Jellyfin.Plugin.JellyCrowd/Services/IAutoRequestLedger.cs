using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Remembers which seasons were already dealt with automatically for each user, so an automatic request
/// is made once per user and season: a cancelled or denied one does not come back on the next episode.
/// </summary>
public interface IAutoRequestLedger
{
  /// <summary>
  /// Whether a user's season was already dealt with.
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="tmdbId">The show's TMDB id.</param>
  /// <param name="season">The season number.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when it was.</returns>
  Task<bool> ContainsAsync(Guid userId, int tmdbId, int season, CancellationToken cancellationToken);

  /// <summary>
  /// Records a user's season as dealt with (idempotent).
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="tmdbId">The show's TMDB id.</param>
  /// <param name="season">The season number.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes once recorded.</returns>
  Task RecordAsync(Guid userId, int tmdbId, int season, CancellationToken cancellationToken);
}
