using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Supplies the seasons and episodes the catalog offers for a show.
/// <para>
/// TMDB and Sonarr do not always agree on how a show is split into seasons: TMDB collapses some anime
/// into a single continuous season (e.g. one season of 59 episodes) while Sonarr — which speaks TVDB,
/// and is what actually downloads — splits the same show into 24 + 23 + 12. Offering TMDB's numbering
/// against a Sonarr backend makes the later seasons unrequestable, so when Sonarr is the backend it is
/// the authority. TMDB remains the source when Sonarr is not configured, or cannot answer.
/// </para>
/// </summary>
public interface ISeriesStructureProvider
{
  /// <summary>
  /// Gets the requestable seasons of a show. Seasons known to hold no episode (an announced-but-empty
  /// season) are left out; a season whose count is merely unknown is kept.
  /// </summary>
  /// <param name="tmdbId">The show's TMDB identifier.</param>
  /// <param name="language">The TMDB language code, used for localized names.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The seasons.</returns>
  Task<IReadOnlyList<Season>> GetSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the episodes of one season. Empty when the season's episodes cannot be offered safely (the
  /// numbering sources disagree and Sonarr does not track the show yet) — the caller then requests the
  /// whole season instead.
  /// </summary>
  /// <param name="tmdbId">The show's TMDB identifier.</param>
  /// <param name="season">The season number, in the numbering <see cref="GetSeasonsAsync"/> returned.</param>
  /// <param name="language">The TMDB language code, used for localized names.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The episodes.</returns>
  Task<IReadOnlyList<Episode>> GetEpisodesAsync(int tmdbId, int season, string language, CancellationToken cancellationToken);
}
