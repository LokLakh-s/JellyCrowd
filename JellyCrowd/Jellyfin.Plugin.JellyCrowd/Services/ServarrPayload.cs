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

    // Set monitoring explicitly on the seasons array (Sonarr honors it across versions): a specific
    // season monitors only that one, otherwise all real seasons (specials = season 0 stay off).
    if (body["seasons"] is JsonArray seasons)
    {
      foreach (var node in seasons)
      {
        if (node is JsonObject seasonObj
            && seasonObj["seasonNumber"] is JsonValue numberValue
            && numberValue.TryGetValue<int>(out var number))
        {
          seasonObj["monitored"] = number > 0 && (season is null || number == season.Value);
        }
      }
    }

    // No "monitor" in addOptions: it would re-derive monitoring and override the seasons array above
    // (e.g. "none" would unmonitor the very season we want, so nothing gets searched).
    body["addOptions"] = new JsonObject
    {
      ["searchForMissingEpisodes"] = true
    };
    return body;
  }

  /// <summary>
  /// Mutates an existing Sonarr series body so the requested season (or every real season when
  /// <paramref name="season"/> is <c>null</c>) is monitored, and the series itself is monitored.
  /// Needed when a show is already in Sonarr from an earlier season: later seasons start unmonitored,
  /// so a season search wouldn't grab anything. Returns <c>true</c> if any flag was changed.
  /// </summary>
  /// <param name="series">The full series resource fetched from Sonarr.</param>
  /// <param name="season">The season to monitor, or <c>null</c> for all real seasons.</param>
  /// <returns><c>true</c> when a monitoring flag was changed (and the series should be persisted).</returns>
  public static bool EnsureSeasonsMonitored(JsonObject series, int? season)
  {
    ArgumentNullException.ThrowIfNull(series);
    var changed = false;

    if (series["monitored"] is not JsonValue rootValue || !rootValue.TryGetValue<bool>(out var rootMonitored) || !rootMonitored)
    {
      series["monitored"] = true;
      changed = true;
    }

    if (series["seasons"] is JsonArray seasons)
    {
      foreach (var node in seasons)
      {
        if (node is not JsonObject seasonObj
            || seasonObj["seasonNumber"] is not JsonValue numberValue
            || !numberValue.TryGetValue<int>(out var number)
            || number <= 0
            || (season is not null && number != season.Value))
        {
          continue;
        }

        var already = seasonObj["monitored"] is JsonValue sv && sv.TryGetValue<bool>(out var b) && b;
        if (!already)
        {
          seasonObj["monitored"] = true;
          changed = true;
        }
      }
    }

    return changed;
  }
}
