using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>A ranked viewer in the statistics.</summary>
public class StatsUserDto
{
  /// <summary>Gets or sets the user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the display name.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>Gets or sets the number of plays.</summary>
  public int Plays { get; set; }

  /// <summary>Gets or sets the total watched minutes.</summary>
  public double Minutes { get; set; }
}
