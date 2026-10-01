using System;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Globalization;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Language codes as the different sources spell them — TMDB's two letters (<c>fr</c>), Jellyfin's tracks
/// and settings in three (<c>fre</c>, <c>fra</c>) — resolved through Jellyfin's own culture table, plus the
/// dub language the "dubbed" and "subtitled" versions refer to.
/// </summary>
public class LanguageCodes
{
  private readonly ILocalizationManager _localization;
  private readonly IServerConfigurationManager _server;

  /// <summary>
  /// Initializes a new instance of the <see cref="LanguageCodes"/> class.
  /// </summary>
  /// <param name="localization">The localization manager (culture table).</param>
  /// <param name="server">The server configuration (preferred metadata language).</param>
  public LanguageCodes(ILocalizationManager localization, IServerConfigurationManager server)
  {
    _localization = localization;
    _server = server;
  }

  /// <summary>
  /// A language as two lowercase letters, from any form (<c>fr</c>, <c>fre</c>, <c>fra</c>, <c>French</c>);
  /// <c>null</c> for an untagged or undetermined track, or an unknown language.
  /// </summary>
  /// <param name="language">The language.</param>
  /// <returns>The two-letter code, or <c>null</c>.</returns>
  public virtual string? TwoLetter(string? language)
  {
    if (string.IsNullOrWhiteSpace(language) || string.Equals(language.Trim(), "und", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var trimmed = language.Trim();
    var two = _localization.FindLanguageInfo(trimmed)?.TwoLetterISOLanguageName;
    if (!string.IsNullOrWhiteSpace(two))
    {
      return two.ToLowerInvariant();
    }

    return trimmed.Length == 2 ? trimmed.ToLowerInvariant() : null;
  }

  /// <summary>
  /// A language as Jellyfin stores it in playback settings and subtitle searches (three letters, e.g. <c>fre</c>).
  /// </summary>
  /// <param name="language">The language.</param>
  /// <returns>The three-letter code, or <c>null</c> when unknown.</returns>
  public virtual string? ThreeLetter(string? language)
    => string.IsNullOrWhiteSpace(language) ? null : _localization.FindLanguageInfo(language.Trim())?.ThreeLetterISOLanguageName;

  /// <summary>
  /// The language's English name from Jellyfin's culture table, or the code itself.
  /// </summary>
  /// <param name="language">The language.</param>
  /// <returns>The name.</returns>
  public virtual string DisplayName(string language)
    => _localization.FindLanguageInfo(language)?.DisplayName ?? language;

  /// <summary>
  /// The dub and subtitle language: the configured one, else the server's preferred metadata language, else English.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <returns>The two-letter code.</returns>
  public virtual string DubLanguage(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    var configured = string.IsNullOrWhiteSpace(config.DubbedLanguage) ? _server.Configuration.PreferredMetadataLanguage : config.DubbedLanguage;
    return TwoLetter(configured) ?? "en";
  }
}
