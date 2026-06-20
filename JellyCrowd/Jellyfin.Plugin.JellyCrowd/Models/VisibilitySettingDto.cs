namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Tells the client whether the plugin should be shown to the current user. Computed server-side from
/// "config mode" (<c>HiddenFromUsers</c>) and the caller's administrator status, so the UI never has to
/// guess who is an admin.
/// </summary>
public class VisibilitySettingDto
{
  /// <summary>
  /// Gets or sets a value indicating whether the plugin is visible to the current user.
  /// </summary>
  public bool Visible { get; set; } = true;
}
