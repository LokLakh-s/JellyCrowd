using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Builds a JSON backup bundle of the plugin's data stores (requests, watchlist, notifications,
/// user preferences). Config is intentionally excluded so the backup carries no secrets (API keys,
/// SMTP credentials). Pure file IO so it can be tested against a temp directory.
/// </summary>
public static class ExportBundle
{
  /// <summary>
  /// Reads the data-store JSON files from <paramref name="dataFolder"/> into one bundle object.
  /// </summary>
  /// <param name="dataFolder">The plugin data folder.</param>
  /// <returns>The bundle: <c>{ requests, watchlist, notifications, prefs }</c>.</returns>
  public static JsonObject Build(string dataFolder)
  {
    ArgumentNullException.ThrowIfNull(dataFolder);
    return new JsonObject
    {
      ["requests"] = Read(dataFolder, "requests.json"),
      ["watchlist"] = Read(dataFolder, "watchlist.json"),
      ["notifications"] = Read(dataFolder, "notifications.json"),
      ["prefs"] = Read(dataFolder, "user-prefs.json")
    };
  }

  private static JsonNode Read(string dataFolder, string file)
  {
    var path = Path.Combine(dataFolder, file);
    if (!File.Exists(path))
    {
      return new JsonArray();
    }

    try
    {
      return JsonNode.Parse(File.ReadAllText(path)) ?? new JsonArray();
    }
#pragma warning disable CA1031 // A corrupt store must not fail the whole backup.
    catch (Exception)
#pragma warning restore CA1031
    {
      return new JsonArray();
    }
  }
}
