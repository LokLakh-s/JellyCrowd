namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A custom entry the admin adds to the Jellyfin left navigation drawer via the Branding settings.
/// Injected into the drawer at runtime by the web client (header.js), so it survives web-client updates
/// without touching jellyfin-web's <c>config.json</c>.
/// </summary>
public class DrawerLink
{
  /// <summary>
  /// Gets or sets the label shown in the drawer.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the URL the entry links to. May be an absolute URL or an in-app hash (e.g. <c>#/home</c>).
  /// </summary>
  public string Url { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the optional Material icon name (e.g. <c>link</c>, <c>movie</c>) shown next to the label.
  /// Empty renders no icon.
  /// </summary>
  public string Icon { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets a value indicating whether the link opens in a new browser tab.
  /// </summary>
  public bool NewTab { get; set; }
}
