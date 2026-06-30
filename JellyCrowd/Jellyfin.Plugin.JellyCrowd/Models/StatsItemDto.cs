namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>A ranked media item (movie or show) in the statistics.</summary>
public class StatsItemDto
{
  /// <summary>Gets or sets the item id (movie item id, or series id for shows).</summary>
  public string ItemId { get; set; } = string.Empty;

  /// <summary>Gets or sets the display name.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>Gets or sets the number of plays.</summary>
  public int Plays { get; set; }

  /// <summary>Gets or sets the total watched minutes.</summary>
  public double Minutes { get; set; }
}
