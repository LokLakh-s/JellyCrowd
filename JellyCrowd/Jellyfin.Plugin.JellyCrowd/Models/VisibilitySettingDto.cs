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

  /// <summary>
  /// Gets or sets a value indicating whether the current user is an administrator (so the client can
  /// show admin-only affordances such as editing the announcement banner).
  /// </summary>
  public bool IsAdmin { get; set; }

  /// <summary>
  /// Gets or sets the announcement text the current user should see, or empty when none applies to them.
  /// Computed per-user so a targeted announcement never leaks to users outside its groups.
  /// </summary>
  public string AnnouncementText { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the announcement severity colour (<c>green</c>, <c>yellow</c> or <c>red</c>).
  /// </summary>
  public string AnnouncementLevel { get; set; } = "green";
}
