using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure parsing of a Radarr/Sonarr <c>/api/v3/indexer</c> response. Network-free so it can be unit tested.
/// </summary>
public static class ServarrIndexerParser
{
  /// <summary>
  /// Counts the usable indexers in the raw Radarr/Sonarr indexers JSON array. An indexer counts as
  /// enabled when any of <c>enableRss</c>, <c>enableAutomaticSearch</c> or <c>enableInteractiveSearch</c>
  /// is <c>true</c> (Radarr/Sonarr have no single <c>enable</c> flag on indexers).
  /// </summary>
  /// <param name="json">The raw indexers JSON.</param>
  /// <returns>The number of enabled indexers (0 when the payload is empty or unparseable).</returns>
  public static int CountEnabled(string? json)
  {
    if (string.IsNullOrWhiteSpace(json))
    {
      return 0;
    }

    JsonNode? node;
    try
    {
      node = JsonNode.Parse(json);
    }
    catch (JsonException)
    {
      return 0;
    }

    if (node is not JsonArray array)
    {
      return 0;
    }

    var count = 0;
    foreach (var item in array)
    {
      if (item is JsonObject obj
        && (IsTrue(obj, "enableRss") || IsTrue(obj, "enableAutomaticSearch") || IsTrue(obj, "enableInteractiveSearch") || IsTrue(obj, "enable")))
      {
        count++;
      }
    }

    return count;
  }

  private static bool IsTrue(JsonObject obj, string property)
    => obj[property] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}
