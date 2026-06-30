namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>A currently-playing Jellyfin session ("now playing"), for the live stats panel.</summary>
public class StatsSessionDto
{
  /// <summary>Gets or sets the viewer's display name.</summary>
  public string UserName { get; set; } = string.Empty;

  /// <summary>Gets or sets the human-readable item label (e.g. "Dune" or "The Office · S2E5").</summary>
  public string Label { get; set; } = string.Empty;

  /// <summary>Gets or sets the item kind (<c>Movie</c>, <c>Episode</c>, …).</summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>Gets or sets the client/app the session is playing on.</summary>
  public string Client { get; set; } = string.Empty;

  /// <summary>Gets or sets the playback position as a percentage of the item's runtime (0–100).</summary>
  public int PositionPercent { get; set; }

  /// <summary>Gets or sets a value indicating whether the session is currently paused.</summary>
  public bool Paused { get; set; }
}
