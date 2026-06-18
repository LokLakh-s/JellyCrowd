namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A single download's live progress, distilled from a Radarr/Sonarr queue record.
/// </summary>
public class QueueProgress
{
  /// <summary>
  /// Gets or sets the simplified state: <c>queued</c>, <c>downloading</c>, <c>importing</c>,
  /// <c>completed</c> or <c>warning</c>.
  /// </summary>
  public string State { get; set; } = "queued";

  /// <summary>
  /// Gets or sets the download completion percentage (0-100).
  /// </summary>
  public double Percent { get; set; }

  /// <summary>
  /// Gets or sets the remaining-time hint as reported by Radarr/Sonarr (e.g. <c>00:12:34</c>), if any.
  /// </summary>
  public string? TimeLeft { get; set; }
}
