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
  /// Gets or sets a value indicating whether the plugin is in "config mode" (hidden from non-admins).
  /// Exposed so the client can hide instantly via a token-free request; the admin check (for whether
  /// the current user is exempt) is done separately against the authenticated visibility endpoint.
  /// </summary>
  public bool Hidden { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether community comments/reviews are enabled (admin opt-in).
  /// </summary>
  public bool CommentsEnabled { get; set; }
}
