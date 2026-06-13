using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure (network-free) parsing of Radarr/Sonarr list payloads (root folders, quality/language
/// profiles) into <see cref="ServarrResource"/> items.
/// </summary>
public static class ServarrResponseParser
{
  /// <summary>
  /// Parses a JSON array of resources, taking each object's <c>id</c> and the given label property
  /// (e.g. <c>path</c> for root folders, <c>name</c> for profiles).
  /// </summary>
  /// <param name="json">The raw JSON array payload.</param>
  /// <param name="labelProperty">The property to use as the display label.</param>
  /// <returns>The parsed resources.</returns>
  public static IReadOnlyList<ServarrResource> ParseResources(string json, string labelProperty)
  {
    ArgumentNullException.ThrowIfNull(json);

    var list = new List<ServarrResource>();
    using var doc = JsonDocument.Parse(json);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
    {
      return list;
    }

    foreach (var element in doc.RootElement.EnumerateArray())
    {
      if (element.ValueKind != JsonValueKind.Object
          || !element.TryGetProperty("id", out var idEl)
          || idEl.ValueKind != JsonValueKind.Number
          || !idEl.TryGetInt32(out var id))
      {
        continue;
      }

      var name = id.ToString(CultureInfo.InvariantCulture);
      if (element.TryGetProperty(labelProperty, out var labelEl) && labelEl.ValueKind == JsonValueKind.String)
      {
        name = labelEl.GetString() ?? name;
      }

      list.Add(new ServarrResource { Id = id, Name = name });
    }

    return list;
  }
}
