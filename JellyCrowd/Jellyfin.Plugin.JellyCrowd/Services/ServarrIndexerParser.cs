using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure parsing of a Radarr/Sonarr <c>/api/v3/indexer</c> response. Network-free so it can be unit tested.
/// </summary>
public static class ServarrIndexerParser
{
  /// <summary>
  /// Counts the enabled indexers in the raw indexers JSON array (items with <c>enable: true</c>).
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
      if (item is JsonObject obj && obj["enable"] is JsonValue v && v.TryGetValue<bool>(out var enabled) && enabled)
      {
        count++;
      }
    }

    return count;
  }
}
