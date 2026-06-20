namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The configured UI/notification language exposed to the user-facing pages.
/// </summary>
public class LanguageSettingDto
{
  /// <summary>
  /// Gets or sets the configured language: <c>"auto"</c> (follow the user) or a 2-letter code.
  /// </summary>
  public string Language { get; set; } = "auto";

  /// <summary>
  /// Gets or sets a value indicating whether the plugin is hidden from regular users ("config mode"):
  /// when <c>true</c>, the header links/quota/bell are not injected for non-administrators. The admin
  /// uses this to keep the plugin out of sight until it is configured and working.
  /// </summary>
  public bool Hidden { get; set; }
}
