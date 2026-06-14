using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Admin payload to edit an existing request (status, season/episode, desired date).
/// </summary>
public class AdminEditRequestDto
{
  /// <summary>
  /// Gets or sets the new status.
  /// </summary>
  public RequestStatus Status { get; set; }

  /// <summary>
  /// Gets or sets the season number (null = movie or whole show).
  /// </summary>
  public int? Season { get; set; }

  /// <summary>
  /// Gets or sets the episode number (null = whole season/movie).
  /// </summary>
  public int? Episode { get; set; }

  /// <summary>
  /// Gets or sets the desired (UTC) fulfillment time.
  /// </summary>
  public DateTime? DesiredAt { get; set; }
}
