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
}
