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
  /// Resolves a configured language to a catalog name. There is no user context when a notification is
  /// built, so <c>auto</c> — which means "follow each user" on the pages — can only mean English here.
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
