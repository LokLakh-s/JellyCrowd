using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Server-side translation lookup. Reads the same embedded <c>Web/strings/&lt;lang&gt;.json</c> catalogs the
/// user pages use, so a notification and the page it refers to are worded identically and adding a
/// language stays a matter of dropping in a catalog file rather than touching code.
/// <para>
/// Catalogs are parsed once and cached: notifications are sent from hot paths and the files never change
/// while the plugin is loaded.
/// </para>
/// </summary>
public static class ServerStrings
{
  /// <summary>The language used when none is configured, or when the configured one has no catalog.</summary>
  public const string FallbackLanguage = "en";

  private const string ResourcePrefix = "Jellyfin.Plugin.JellyCrowd.Web.strings.";

  private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new(StringComparer.Ordinal);

  /// <summary>
  /// Builds a lookup for a language. Unknown keys fall back to English and then to the key itself, so a
  /// missing translation degrades to readable text instead of an empty notification.
  /// </summary>
  /// <param name="language">The configured language (<c>en</c>, <c>fr</c>, <c>auto</c>, or null/unknown).</param>
  /// <returns>A key-to-text function.</returns>
  public static Func<string, string> For(string? language)
  {
    var lang = Normalize(language);
    var catalog = Load(lang);
    var fallback = string.Equals(lang, FallbackLanguage, StringComparison.Ordinal) ? catalog : Load(FallbackLanguage);

    return key =>
    {
      if (key is null)
      {
        return string.Empty;
      }

      if (catalog.TryGetValue(key, out var text) && !string.IsNullOrEmpty(text))
      {
        return text;
      }

      return fallback.TryGetValue(key, out var english) && !string.IsNullOrEmpty(english) ? english : key;
    };
  }

  /// <summary>
  /// Builds the lookup for a message to one member: the configured language when the administrator forced
  /// one, otherwise the language their pages are shown in (see <see cref="MemberLanguages"/>), else the
  /// server's.
  /// </summary>
  /// <param name="configured">The configured language (<c>auto</c> or a code).</param>
  /// <param name="userId">The recipient.</param>
  /// <returns>A key-to-text function.</returns>
  public static Func<string, string> ForMember(string? configured, Guid userId)
    => For(Resolve(configured, MemberLanguages.Get(userId), MemberLanguages.ServerLanguage));

  /// <summary>
  /// Builds the lookup for what the administrators read (Discord, the ops mailbox, report alerts): the
  /// configured language when forced, otherwise the server's.
  /// </summary>
  /// <param name="configured">The configured language (<c>auto</c> or a code).</param>
  /// <returns>A key-to-text function.</returns>
  public static Func<string, string> ForStaff(string? configured)
    => For(Resolve(configured, null, MemberLanguages.ServerLanguage));

  /// <summary>
  /// Picks the language of a message: a forced configuration wins; under <c>auto</c>, the member's language
  /// when there is a catalog for it, then the server's, then English.
  /// </summary>
  /// <param name="configured">The configured language (<c>auto</c>, blank, or a code).</param>
  /// <param name="memberLanguage">The recipient's language, if known.</param>
  /// <param name="serverLanguage">The Jellyfin server's display language, if known.</param>
  /// <returns>A two-letter catalog name.</returns>
  public static string Resolve(string? configured, string? memberLanguage, string? serverLanguage)
  {
    if (!string.IsNullOrWhiteSpace(configured) && !string.Equals(configured.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
    {
      return Normalize(configured);
    }

    if (HasCatalog(memberLanguage))
    {
      return Normalize(memberLanguage);
    }

    return HasCatalog(serverLanguage) ? Normalize(serverLanguage) : FallbackLanguage;
  }

  /// <summary>
  /// Determines whether a language has a translation catalog.
  /// </summary>
  /// <param name="language">A code or locale (e.g. <c>fr</c>, <c>fr-FR</c>).</param>
  /// <returns><c>true</c> when the plugin ships a catalog for it.</returns>
  public static bool HasCatalog(string? language)
    => !string.IsNullOrWhiteSpace(language) && Load(Normalize(language)).Count > 0;

  /// <summary>
  /// Resolves a configured language to a catalog name; anything that is not a language code (<c>auto</c>,
  /// blank) gives English. Use <see cref="ForMember"/> or <see cref="ForStaff"/> to honour <c>auto</c>.
  /// </summary>
  /// <param name="language">The configured language.</param>
  /// <returns>A two-letter catalog name.</returns>
  public static string Normalize(string? language)
  {
    if (string.IsNullOrWhiteSpace(language))
    {
      return FallbackLanguage;
    }

    // Accept "fr-FR" as well as "fr": the config offers plain codes, but a locale should not break it.
    var lang = language.Trim().ToLowerInvariant();
    var dash = lang.IndexOf('-', StringComparison.Ordinal);
    if (dash > 0)
    {
      lang = lang[..dash];
    }

    return lang.Length == 2 ? lang : FallbackLanguage;
  }

  private static IReadOnlyDictionary<string, string> Load(string language)
    => Catalogs.GetOrAdd(language, ReadCatalog);

  private static IReadOnlyDictionary<string, string> ReadCatalog(string language)
  {
    var empty = new Dictionary<string, string>(StringComparer.Ordinal);
    try
    {
      var assembly = Assembly.GetExecutingAssembly();
      using var stream = assembly.GetManifestResourceStream(ResourcePrefix + language + ".json");
      if (stream is null)
      {
        return empty;
      }

      using var reader = new StreamReader(stream);
      var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
      return parsed is null ? empty : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
    }
#pragma warning disable CA1031 // A broken catalog must degrade to English, never break a notification.
    catch (Exception)
#pragma warning restore CA1031
    {
      return empty;
    }
  }
}
