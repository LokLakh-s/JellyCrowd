namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One TMDB season and the Sonarr season holding it.
/// </summary>
public class SeasonLink
{
  /// <summary>
  /// Gets or sets the TMDB season number.
  /// </summary>
  public int TmdbSeason { get; set; }

  /// <summary>
  /// Gets or sets the Sonarr (TVDB) season number.
  /// </summary>
  public int SonarrSeason { get; set; }
}
