using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Live download status for one request, surfaced to the "My requests" view.
/// </summary>
public class DownloadStatusDto
{
  /// <summary>
  /// Gets or sets the request this status belongs to.
  /// </summary>
  public Guid RequestId { get; set; }

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
  /// Gets or sets the remaining-time hint as reported by Radarr/Sonarr, if any.
  /// </summary>
  public string? TimeLeft { get; set; }
}
