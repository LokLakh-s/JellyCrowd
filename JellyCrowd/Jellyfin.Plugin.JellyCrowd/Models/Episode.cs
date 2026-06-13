namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A TMDB episode of a show's season.
/// </summary>
public class Episode
{
  /// <summary>
  /// Gets or sets the season number.
  /// </summary>
  public int SeasonNumber { get; set; }

  /// <summary>
  /// Gets or sets the episode number within the season.
  /// </summary>
  public int EpisodeNumber { get; set; }

  /// <summary>
  /// Gets or sets the localized episode name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the air date (ISO <c>yyyy-MM-dd</c> string), when known.
  /// </summary>
  public string? AirDate { get; set; }

  /// <summary>
  /// Gets or sets the TMDB relative still-image path, when known.
  /// </summary>
  public string? StillPath { get; set; }
}
