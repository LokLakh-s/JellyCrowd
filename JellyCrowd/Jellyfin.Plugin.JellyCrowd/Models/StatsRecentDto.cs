using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>A recent play entry in the statistics.</summary>
public class StatsRecentDto
{
  /// <summary>Gets or sets the viewer's display name.</summary>
  public string UserName { get; set; } = string.Empty;

  /// <summary>Gets or sets the human-readable item label (e.g. "Dune" or "The Office · S2E5").</summary>
  public string Label { get; set; } = string.Empty;

  /// <summary>Gets or sets the item kind (<c>Movie</c>, <c>Episode</c>, …).</summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>Gets or sets the UTC time the play started.</summary>
  public DateTime PlayedAtUtc { get; set; }

  /// <summary>Gets or sets the watched minutes.</summary>
  public double Minutes { get; set; }
}
