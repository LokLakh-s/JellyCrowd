namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A TMDB season of a show.
/// </summary>
public class Season
{
  /// <summary>
  /// Gets or sets the season number (0 is specials).
  /// </summary>
  public int SeasonNumber { get; set; }

  /// <summary>
  /// Gets or sets the localized season name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the number of episodes in the season.
  /// </summary>
  public int EpisodeCount { get; set; }
}
