using System;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure builders turning a Radarr/Sonarr lookup result into the body posted back to add the item.
/// They start from the lookup object (which already carries title, ids, images, seasons…) and layer
/// on the admin's root folder, quality profile and "search now" options.
/// </summary>
public static class ServarrPayload
{
  /// <summary>
  /// Builds the Radarr "add movie" body from a movie lookup object.
  /// </summary>
  /// <param name="lookup">The Radarr movie lookup object.</param>
  /// <param name="qualityProfileId">The quality profile id to apply.</param>
  /// <param name="rootFolderPath">The root folder path.</param>
  /// <returns>The body to POST to <c>/api/v3/movie</c>.</returns>
  public static JsonObject BuildMovieAdd(JsonObject lookup, int qualityProfileId, string rootFolderPath)
  {
    ArgumentNullException.ThrowIfNull(lookup);

    var body = (JsonObject)lookup.DeepClone();
    body["qualityProfileId"] = qualityProfileId;
    body["rootFolderPath"] = rootFolderPath;
    body["monitored"] = true;
    body["minimumAvailability"] = "released";
    body["addOptions"] = new JsonObject { ["searchForMovie"] = true };
    return body;
  }

  /// <summary>
  /// Builds the Sonarr "add series" body from a series lookup object. When a specific season is
  /// requested, only that season is monitored; otherwise the whole series is.
  /// </summary>
  /// <param name="lookup">The Sonarr series lookup object.</param>
  /// <param name="qualityProfileId">The quality profile id to apply.</param>
  /// <param name="languageProfileId">The language profile id (Sonarr v3); ignored when &lt;= 0.</param>
  /// <param name="rootFolderPath">The root folder path.</param>
  /// <param name="season">The requested season number, or <c>null</c> for the whole series.</param>
  /// <returns>The body to POST to <c>/api/v3/series</c>.</returns>
  public static JsonObject BuildSeriesAdd(JsonObject lookup, int qualityProfileId, int languageProfileId, string rootFolderPath, int? season)
  {
    ArgumentNullException.ThrowIfNull(lookup);

    var body = (JsonObject)lookup.DeepClone();
    body["qualityProfileId"] = qualityProfileId;
    if (languageProfileId > 0)
    {
      body["languageProfileId"] = languageProfileId;
    }

    body["rootFolderPath"] = rootFolderPath;
    body["monitored"] = true;
    body["seasonFolder"] = true;

    var monitorAll = season is null;
    if (season is { } requested && body["seasons"] is JsonArray seasons)
    {
      foreach (var node in seasons)
      {
        if (node is JsonObject seasonObj
            && seasonObj["seasonNumber"] is JsonValue numberValue
            && numberValue.TryGetValue<int>(out var number))
        {
          seasonObj["monitored"] = number == requested;
        }
      }
    }

    body["addOptions"] = new JsonObject
    {
      ["searchForMissingEpisodes"] = true,
      ["monitor"] = monitorAll ? "all" : "none"
    };
    return body;
  }
}
