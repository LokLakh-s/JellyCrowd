namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A selectable Radarr/Sonarr resource (a root folder or a quality/language profile): an id plus a
/// human-readable label, used to populate the admin dropdowns.
/// </summary>
public sealed class ServarrResource
{
  /// <summary>
  /// Gets or sets the resource id.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  /// Gets or sets the display label (a folder path or a profile name).
  /// </summary>
  public string Name { get; set; } = string.Empty;
}
