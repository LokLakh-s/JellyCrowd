namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The automatic next-season request as the current user sees it: whether it is offered, whether they
/// opted in, and how close to the end of a season it kicks in.
/// </summary>
public class AutoNextSeasonDto
{
  /// <summary>
  /// Gets or sets a value indicating whether the administrator offers the feature.
  /// </summary>
  public bool Available { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the current user opted in.
  /// </summary>
  public bool Enabled { get; set; }

  /// <summary>
  /// Gets or sets how many episodes may remain in the season when the next one is requested.
  /// </summary>
  public int EpisodesLeft { get; set; }
}
