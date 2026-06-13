using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure parser for the webhook's optional HTTP headers, configured as one <c>Name: Value</c> per line.
/// </summary>
public static class WebhookHeaders
{
  /// <summary>
  /// Parses the configured header block into name/value pairs. Blank lines are ignored; the first
  /// colon separates the name from the value; both are trimmed. Lines without a colon or with an
  /// empty name are skipped.
  /// </summary>
  /// <param name="raw">The raw configured header text (may be null/empty).</param>
  /// <returns>The parsed headers, in order.</returns>
  public static IReadOnlyList<KeyValuePair<string, string>> Parse(string? raw)
  {
    var result = new List<KeyValuePair<string, string>>();
    if (string.IsNullOrWhiteSpace(raw))
    {
      return result;
    }

    var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    foreach (var line in lines)
    {
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      var colon = line.IndexOf(':', StringComparison.Ordinal);
      if (colon <= 0)
      {
        continue;
      }

      var name = line[..colon].Trim();
      var value = line[(colon + 1)..].Trim();
      if (name.Length > 0)
      {
        result.Add(new KeyValuePair<string, string>(name, value));
      }
    }

    return result;
  }
}
