namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A season of a show, as offered in the catalog.
/// </summary>
public class Season
{
  /// <summary>
  /// Gets or sets the season number (0 is specials).
  /// </summary>
  public int SeasonNumber { get; set; }

  /// <summary>
  /// Gets or sets the localized season name. Empty when the source has no name for it (the client then
  /// renders a localized "Season N").
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the number of episodes in the season, or <c>null</c> when it is not known — which is
  /// not the same as zero. Sonarr only reports counts for series it already tracks, whereas a count of
  /// zero means an announced-but-empty season, which is never worth offering.
  /// </summary>
  public int? EpisodeCount { get; set; }
}
