using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// The language each member's Jelly Crowd pages are shown in, as their browser last reported it, kept in
/// memory so a notification can be worded for its recipient wherever it is composed. Fed by the
/// preferences store, which persists it; the server's own display language is the fallback for a member
/// who has not opened a page since.
/// </summary>
public static class MemberLanguages
{
  private static readonly ConcurrentDictionary<Guid, string> Known = new();

  /// <summary>
  /// Gets or sets the source of the Jellyfin server's display language (e.g. <c>fr-FR</c>), set at startup.
  /// </summary>
  public static Func<string?>? ServerLanguageSource { get; set; }

  /// <summary>
  /// Gets the Jellyfin server's display language, when known.
  /// </summary>
  public static string? ServerLanguage
  {
    get
    {
      try
      {
        return ServerLanguageSource?.Invoke();
      }
#pragma warning disable CA1031 // A configuration hiccup must degrade to the fallback, never break a notification.
      catch (Exception)
#pragma warning restore CA1031
      {
        return null;
      }
    }
  }

  /// <summary>
  /// Gets a member's language, or <c>null</c> when they have not reported one.
  /// </summary>
  /// <param name="userId">The member.</param>
  /// <returns>A two-letter language code, or <c>null</c>.</returns>
  public static string? Get(Guid userId) => Known.TryGetValue(userId, out var language) ? language : null;

  /// <summary>
  /// Records a member's language; a blank one forgets it.
  /// </summary>
  /// <param name="userId">The member.</param>
  /// <param name="language">Their two-letter language code.</param>
  public static void Remember(Guid userId, string? language)
  {
    if (string.IsNullOrWhiteSpace(language))
    {
      Known.TryRemove(userId, out _);
    }
    else
    {
      Known[userId] = language;
    }
  }
}
