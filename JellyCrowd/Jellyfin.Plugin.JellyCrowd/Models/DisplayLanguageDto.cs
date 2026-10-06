namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The language a member's pages are shown in, reported by their browser.
/// </summary>
public class DisplayLanguageDto
{
  /// <summary>
  /// Gets or sets the language code or locale (e.g. <c>fr</c> or <c>fr-FR</c>).
  /// </summary>
  public string? Language { get; set; }
}
