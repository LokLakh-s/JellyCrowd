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
  /// Gets the on-disk size (in bytes) of the matching library item(s), summing episodes for shows.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <returns>The total size in bytes, or 0 when nothing matches.</returns>
  long GetSizeBytes(string mediaType, int tmdbId);
}
