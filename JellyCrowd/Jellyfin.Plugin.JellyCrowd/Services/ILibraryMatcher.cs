using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Checks whether a TMDB title already exists in the Jellyfin library.
/// </summary>
public interface ILibraryMatcher
{
  /// <summary>
  /// Determines whether a movie or show with the given TMDB id is present in the library.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <returns><c>true</c> when a matching library item exists.</returns>
  bool Exists(string mediaType, int tmdbId);

  /// <summary>
  /// Finds the Jellyfin library item id (32-char hex) for a TMDB title, or <c>null</c> if absent.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <returns>The matching item id, or <c>null</c>.</returns>
  string? FindItemId(string mediaType, int tmdbId);

  /// <summary>
  /// Finds the Jellyfin item id for a specific episode (or season) of a show identified by its TMDB id.
  /// Used to mark per-episode / per-season requests available only when that exact episode (or, when
  /// <paramref name="episode"/> is <c>null</c>, any episode of that season) is actually in the library —
  /// the series merely existing is not enough.
  /// </summary>
  /// <param name="seriesTmdbId">The show's TMDB identifier.</param>
  /// <param name="season">The season number, or <c>null</c> to match the series itself.</param>
  /// <param name="episode">The episode number, or <c>null</c> to match any episode of the season.</param>
  /// <returns>The matching episode/series item id, or <c>null</c> when not present.</returns>
  string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode);

  /// <summary>
  /// Finds the Jellyfin item id of a show's season (for deleting just that season's folder), or <c>null</c>.
  /// </summary>
  /// <param name="seriesTmdbId">The show's TMDB identifier.</param>
  /// <param name="season">The season number.</param>
  /// <returns>The season item id, or <c>null</c> when not present.</returns>
  string? FindSeasonItemId(int seriesTmdbId, int season);

  /// <summary>
  /// Gets the on-disk size (in bytes) of the matching library item(s), summing episodes for shows.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <returns>The total size in bytes, or 0 when nothing matches.</returns>
  long GetSizeBytes(string mediaType, int tmdbId);

  /// <summary>
  /// Gets the on-disk size (in bytes) of a specific season/episode of a show (or the whole title when
  /// <paramref name="season"/> is <c>null</c>), so per-season/episode requests count only their own space.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <param name="season">The season number, or <c>null</c> for the whole title.</param>
  /// <param name="episode">The episode number, or <c>null</c> for the whole season.</param>
  /// <returns>The size in bytes, or 0 when nothing matches.</returns>
  long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode);

  /// <summary>
  /// Lists every movie/show in the library that carries a TMDB id (id, type, title, size), for the
  /// admin library-cleanup tool. <see cref="LibraryMediaItem.OwnerCount"/> is left for the caller to fill.
  /// </summary>
  /// <returns>The library media items.</returns>
  IReadOnlyList<LibraryMediaItem> ListLibraryMedia();
}
