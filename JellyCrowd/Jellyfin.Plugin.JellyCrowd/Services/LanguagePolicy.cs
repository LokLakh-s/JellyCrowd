using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// What a delivered title lacks against the version its requester prefers — the short notice they get.
/// </summary>
public enum LanguageNotice
{
  /// <summary>The preference is met, or there is no telling.</summary>
  None,

  /// <summary>Only the dub was found; the original audio is missing.</summary>
  DubbedOnly,

  /// <summary>Only the original audio was found; the dub is missing.</summary>
  OriginalOnly,

  /// <summary>The original audio is there, without subtitles in the user's language.</summary>
  NoSubtitles,
}

/// <summary>
/// Pure rules behind language preferences: the Radarr/Sonarr profile a title gets from its requesters, the
/// notice a requester gets when the delivered file misses their version, and the Jellyfin playback settings
/// a preference stands for. Language codes are ISO 639-1 (two letters), compared case-insensitively.
/// </summary>
public static class LanguagePolicy
{
  /// <summary>The original version.</summary>
  public const string Original = "original";

  /// <summary>The dubbed version.</summary>
  public const string Dubbed = "dubbed";

  /// <summary>The original version with subtitles.</summary>
  public const string Subtitled = "subtitled";

  /// <summary>
  /// Normalizes a stored or posted preference: one of the three versions, or empty for none.
  /// </summary>
  /// <param name="preference">The raw preference.</param>
  /// <returns>The normalized preference.</returns>
  public static string Normalize(string? preference)
  {
    var p = (preference ?? string.Empty).Trim().ToLowerInvariant();
    return p is Original or Dubbed or Subtitled ? p : string.Empty;
  }

  /// <summary>
  /// The profile a preference maps to: its own when the administrator set one, otherwise the common one.
  /// </summary>
  /// <param name="preference">The preference.</param>
  /// <param name="original">The profile for the original version (0 = none).</param>
  /// <param name="dubbed">The profile for the dubbed version (0 = none).</param>
  /// <param name="subtitled">The profile for the original with subtitles (0 = none).</param>
  /// <param name="common">The common profile.</param>
  /// <returns>The profile id.</returns>
  public static int ProfileFor(string? preference, int original, int dubbed, int subtitled, int common)
  {
    var specific = Normalize(preference) switch
    {
      Original => original,
      Dubbed => dubbed,
      Subtitled => subtitled,
      _ => 0
    };
    return specific > 0 ? specific : common;
  }

  /// <summary>
  /// The profile a title gets from the preferences of everyone who asked for it. Requesters without a
  /// preference take whatever comes; the others agree on one profile, or the title falls back to the common
  /// profile (the one meant to satisfy everybody, e.g. original + dub). With no preference at all there is
  /// nothing to decide: <c>null</c>, and whatever profile the title has stays.
  /// </summary>
  /// <param name="requesterPreferences">The preferences of the title's requesters.</param>
  /// <param name="profileFor">Maps a preference to its profile.</param>
  /// <param name="common">The common profile.</param>
  /// <returns>The profile id, or <c>null</c> when no requester has a preference.</returns>
  public static int? TargetProfile(IEnumerable<string?> requesterPreferences, Func<string, int> profileFor, int common)
  {
    ArgumentNullException.ThrowIfNull(requesterPreferences);
    ArgumentNullException.ThrowIfNull(profileFor);
    var wanted = requesterPreferences
      .Select(Normalize)
      .Where(p => p.Length > 0)
      .Select(profileFor)
      .Distinct()
      .ToList();
    return wanted.Count switch
    {
      0 => null,
      1 => wanted[0],
      _ => common
    };
  }

  /// <summary>
  /// What a delivered file lacks for a preference. Says nothing when it cannot be sure: no preference, an
  /// unknown original language, an untagged or missing audio track, or a title whose original language is
  /// the dub language (the original IS the dub).
  /// </summary>
  /// <param name="preference">The requester's preference.</param>
  /// <param name="originalLanguage">The title's original language.</param>
  /// <param name="audioLanguages">The file's audio track languages (<c>null</c> for an untagged track).</param>
  /// <param name="subtitleLanguages">The file's subtitle languages, embedded and external.</param>
  /// <param name="dubLanguage">The dub and subtitle language.</param>
  /// <returns>The notice to give, or <see cref="LanguageNotice.None"/>.</returns>
  public static LanguageNotice Evaluate(
    string? preference,
    string? originalLanguage,
    IReadOnlyCollection<string?> audioLanguages,
    IReadOnlyCollection<string?> subtitleLanguages,
    string dubLanguage)
  {
    ArgumentNullException.ThrowIfNull(audioLanguages);
    ArgumentNullException.ThrowIfNull(subtitleLanguages);
    var pref = Normalize(preference);
    if (pref.Length == 0
        || string.IsNullOrWhiteSpace(originalLanguage)
        || string.IsNullOrWhiteSpace(dubLanguage)
        || audioLanguages.Count == 0
        || audioLanguages.Any(string.IsNullOrWhiteSpace)
        || Same(originalLanguage, dubLanguage))
    {
      return LanguageNotice.None;
    }

    var hasOriginal = audioLanguages.Any(l => Same(l, originalLanguage));
    var hasDub = audioLanguages.Any(l => Same(l, dubLanguage));
    var hasSubtitles = subtitleLanguages.Any(l => Same(l, dubLanguage));
    return pref switch
    {
      Original when !hasOriginal && hasDub => LanguageNotice.DubbedOnly,
      Dubbed when !hasDub && hasOriginal => LanguageNotice.OriginalOnly,
      Subtitled when !hasOriginal && hasDub => LanguageNotice.DubbedOnly,
      Subtitled when hasOriginal && !hasSubtitles => LanguageNotice.NoSubtitles,
      _ => LanguageNotice.None
    };
  }

  /// <summary>
  /// The subtitle languages to fetch for a requester: those they picked, plus the dub language when they
  /// prefer the original with subtitles. Normalized, without duplicates.
  /// </summary>
  /// <param name="preference">The requester's preference.</param>
  /// <param name="picked">The subtitle languages they picked.</param>
  /// <param name="dubLanguage">The dub and subtitle language.</param>
  /// <returns>The languages to fetch.</returns>
  public static IReadOnlyList<string> SubtitlesWanted(string? preference, IEnumerable<string>? picked, string dubLanguage)
  {
    var wanted = (picked ?? Enumerable.Empty<string>()).ToList();
    if (Normalize(preference) == Subtitled && !string.IsNullOrWhiteSpace(dubLanguage))
    {
      wanted.Insert(0, dubLanguage);
    }

    return wanted
      .Where(l => !string.IsNullOrWhiteSpace(l))
      .Select(l => l.Trim().ToLowerInvariant())
      .Distinct(StringComparer.Ordinal)
      .ToList();
  }

  /// <summary>
  /// The Jellyfin playback settings a preference stands for: the dub plays the dub-language track; the
  /// original plays the file's default track; the original with subtitles does too, and always shows
  /// subtitles in the dub language. Without a preference the audio settings are left alone. Picked subtitle
  /// languages otherwise set the preferred subtitle language (the subtitle mode is left alone).
  /// </summary>
  /// <param name="preference">The preference.</param>
  /// <param name="dubLanguage3">The dub language as Jellyfin stores it (three letters, e.g. <c>fre</c>).</param>
  /// <param name="firstPickedSubtitle3">The first picked subtitle language (three letters), if any.</param>
  /// <returns>The settings to write; a <c>null</c> field or a <c>false</c> flag leaves the current value.</returns>
  public static PlaybackSettings PlaybackFor(string? preference, string dubLanguage3, string? firstPickedSubtitle3)
  {
    return Normalize(preference) switch
    {
      Dubbed => new PlaybackSettings(true, dubLanguage3, false, firstPickedSubtitle3, false),
      Original => new PlaybackSettings(true, null, true, firstPickedSubtitle3, false),
      Subtitled => new PlaybackSettings(true, null, true, dubLanguage3, true),
      _ => new PlaybackSettings(false, null, true, firstPickedSubtitle3, false)
    };
  }

  private static bool Same(string? a, string? b)
    => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
