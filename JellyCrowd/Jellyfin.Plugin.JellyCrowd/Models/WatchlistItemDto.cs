namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Payload to add or remove a watchlist entry for the current user.
/// </summary>
public class WatchlistItemDto
{
  /// <summary>
  /// Gets or sets the TMDB identifier.
  /// </summary>
  public int TmdbId { get; set; }

  /// <summary>
  /// Gets or sets the media type, either <c>movie</c> or <c>tv</c>.
  /// </summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the display title (used when adding).
  /// </summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TMDB relative poster path (used when adding).
  /// </summary>
  public string? PosterPath { get; set; }

  /// <summary>
  /// Gets or sets the release/first-air date (used when adding).
  /// </summary>
  public string? ReleaseDate { get; set; }
}
