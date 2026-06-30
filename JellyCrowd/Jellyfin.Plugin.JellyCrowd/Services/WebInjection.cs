using System;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// The web-client injection: the one <c>&lt;script&gt;</c> tag Jelly Crowd adds to <c>index.html</c> to load
/// its shell (<c>header.js</c>). Pure string work, shared by the request-time middleware so it can be unit
/// tested without an HTTP context.
/// </summary>
public static class WebInjection
{
  /// <summary>The URL of the shell script served by <c>WebController</c>.</summary>
  public const string HeaderScriptUrl = "/JellyCrowd/Web/header.js";

  /// <summary>The script tag inserted into <c>index.html</c>.</summary>
  public const string ScriptTag = "<script src=\"" + HeaderScriptUrl + "\" defer></script>";

  /// <summary>
  /// Inserts the shell script tag just before the final <c>&lt;/body&gt;</c>. Idempotent (returns the input
  /// unchanged when the tag is already present) and a no-op when there is no <c>&lt;/body&gt;</c>.
  /// </summary>
  /// <param name="html">The page HTML.</param>
  /// <returns>The HTML with the script injected.</returns>
  public static string InjectScript(string html)
  {
    if (string.IsNullOrEmpty(html) || html.Contains(HeaderScriptUrl, StringComparison.Ordinal))
    {
      return html ?? string.Empty;
    }

    var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
    return index < 0 ? html : html.Insert(index, ScriptTag);
  }
}
