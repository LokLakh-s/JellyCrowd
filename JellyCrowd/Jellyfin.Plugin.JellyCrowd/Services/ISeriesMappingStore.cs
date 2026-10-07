using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// The TMDB shows Sonarr files under another series or season (see <see cref="SeriesMapping"/>). Reads are
/// synchronous, from memory, so the library matcher can consult it on every lookup.
/// </summary>
public interface ISeriesMappingStore
{
  /// <summary>
  /// Gets the mapping of a TMDB show, or <c>null</c> when Sonarr files it as TMDB does.
  /// </summary>
  /// <param name="tmdbId">The TMDB show id.</param>
  /// <returns>The mapping, or <c>null</c>.</returns>
  SeriesMapping? Get(int tmdbId);

  /// <summary>
  /// Gets the mappings that point into a TVDB series (the TMDB shows filed under it).
  /// </summary>
  /// <param name="tvdbId">The TVDB series id.</param>
  /// <returns>The mappings, possibly none.</returns>
  IReadOnlyList<SeriesMapping> ForTvdb(int tvdbId);

  /// <summary>
  /// Loads the stored mappings into memory (once, at startup).
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes once loaded.</returns>
  Task LoadAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Saves (replaces) a TMDB show's mapping.
  /// </summary>
  /// <param name="mapping">The mapping.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes once persisted.</returns>
  Task SetAsync(SeriesMapping mapping, CancellationToken cancellationToken);

  /// <summary>
  /// Forgets a TMDB show's mapping (its seasons no longer line up).
  /// </summary>
  /// <param name="tmdbId">The TMDB show id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes once persisted.</returns>
  Task RemoveAsync(int tmdbId, CancellationToken cancellationToken);
}
