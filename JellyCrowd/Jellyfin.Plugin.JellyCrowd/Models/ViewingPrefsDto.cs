using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The current user's viewing preferences: preferred version and subtitle languages, with what the
/// administrator offers.
/// </summary>
public class ViewingPrefsDto
{
  /// <summary>
  /// Gets or sets a value indicating whether the administrator offers a preferred version.
  /// </summary>
  public bool LanguagePreferencesAvailable { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the administrator offers automatic subtitles.
  /// </summary>
  public bool SubtitleDownloadsAvailable { get; set; }

  /// <summary>
  /// Gets or sets the dub and subtitle language of the "dubbed" and "original with subtitles" versions (ISO 639-1).
  /// </summary>
  public string DubLanguage { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the preferred version: <c>original</c>, <c>dubbed</c>, <c>subtitled</c>, or empty.
  /// </summary>
  public string LanguagePreference { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the subtitle languages to add (ISO 639-1).
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Bound from the posted JSON.")]
  public Collection<string> SubtitleLanguages { get; set; } = new();
}
