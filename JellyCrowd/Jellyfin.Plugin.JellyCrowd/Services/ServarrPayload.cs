using System;
using System.Collections.Generic;
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
    => BuildSeriesAdd(lookup, qualityProfileId, languageProfileId, rootFolderPath, season, inert: false);

  /// <summary>
  /// Builds a Sonarr "add series" body that monitors nothing: the series is added only so its episodes can be
  /// compared with TMDB's before anything of it is monitored (see <c>SeasonAlignment</c>).
  /// </summary>
  /// <param name="lookup">The Sonarr series lookup object.</param>
  /// <param name="qualityProfileId">The quality profile id to apply.</param>
  /// <param name="languageProfileId">The language profile id (Sonarr v3); ignored when &lt;= 0.</param>
  /// <param name="rootFolderPath">The root folder path.</param>
  /// <returns>The body to POST to <c>/api/v3/series</c>.</returns>
  public static JsonObject BuildInertSeriesAdd(JsonObject lookup, int qualityProfileId, int languageProfileId, string rootFolderPath)
    => BuildSeriesAdd(lookup, qualityProfileId, languageProfileId, rootFolderPath, null, inert: true);

  private static JsonObject BuildSeriesAdd(JsonObject lookup, int qualityProfileId, int languageProfileId, string rootFolderPath, int? season, bool inert)
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

    // Only a whole-series request follows the seasons Sonarr lists later; a season or an episode request
    // must not have the next season monitored (and downloaded) for nobody.
    body["monitorNewItems"] = season is null && !inert ? "all" : "none";

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
          seasonObj["monitored"] = !inert && number > 0 && (season is null || number == season.Value);
        }
      }
    }

    // Add the series with NOTHING monitored and NO search. Sonarr's addOptions.monitor always re-derives
    // monitoring and overrides the seasons array above — and when it is omitted it defaults to "all", so
    // relying on the array made a single-season request monitor every season and searchForMissingEpisodes
    // then grabbed the entire show. Instead we add it inert, then the caller monitors only the requested
    // season and issues a targeted SeasonSearch (same path as a series already in Sonarr).
    body["addOptions"] = new JsonObject
    {
      ["monitor"] = "none",
      ["searchForMissingEpisodes"] = false
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

  /// <summary>
  /// Mutates an existing Sonarr series body so the series itself is monitored, leaving every season flag as
  /// it is. For a single-episode request: Sonarr only grabs an episode of a monitored series, but turning
  /// the episode's season on would make it monitor every episode of that season (and each one it lists
  /// later). Returns <c>true</c> if the flag changed.
  /// </summary>
  /// <param name="series">The full series resource fetched from Sonarr.</param>
  /// <returns><c>true</c> when the series' monitored flag was changed.</returns>
  public static bool EnsureSeriesMonitored(JsonObject series)
  {
    ArgumentNullException.ThrowIfNull(series);
    if (series["monitored"] is JsonValue rootValue && rootValue.TryGetValue<bool>(out var rootMonitored) && rootMonitored)
    {
      return false;
    }

    series["monitored"] = true;
    return true;
  }

  /// <summary>
  /// Mutates an existing Sonarr series body so Sonarr does — or does not — monitor the seasons it lists
  /// from now on (<c>monitorNewItems</c>). Returns <c>true</c> if the setting changed.
  /// </summary>
  /// <param name="series">The full series resource fetched from Sonarr.</param>
  /// <param name="follow">Whether new seasons should be monitored.</param>
  /// <returns><c>true</c> when the setting was changed.</returns>
  public static bool SetFollowsNewSeasons(JsonObject series, bool follow)
  {
    ArgumentNullException.ThrowIfNull(series);
    var wanted = follow ? "all" : "none";
    var current = series["monitorNewItems"] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
    if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    series["monitorNewItems"] = wanted;
    return true;
  }

  /// <summary>
  /// Lists the seasons a Sonarr series currently monitors.
  /// </summary>
  /// <param name="series">The full series resource fetched from Sonarr.</param>
  /// <returns>The monitored season numbers.</returns>
  public static IReadOnlyList<int> MonitoredSeasons(JsonObject series)
  {
    ArgumentNullException.ThrowIfNull(series);
    var seasons = new List<int>();
    if (series["seasons"] is JsonArray array)
    {
      foreach (var node in array)
      {
        if (node is JsonObject seasonObj
            && seasonObj["seasonNumber"] is JsonValue numberValue
            && numberValue.TryGetValue<int>(out var number)
            && seasonObj["monitored"] is JsonValue sv && sv.TryGetValue<bool>(out var monitored) && monitored)
        {
          seasons.Add(number);
        }
      }
    }

    return seasons;
  }

  /// <summary>
  /// Read-only check that Sonarr has finished processing a fresh add. Its post-add step (which applies the
  /// add's monitoring option — "none" here, unmonitoring everything) clears the series' <c>addOptions</c>
  /// when it is done, so monitoring applied after that point is no longer overwritten.
  /// </summary>
  /// <param name="series">The full series resource fetched from Sonarr.</param>
  /// <returns><c>true</c> when the series carries no pending add options.</returns>
  public static bool IsPostAddComplete(JsonObject series)
  {
    ArgumentNullException.ThrowIfNull(series);
    return series["addOptions"] is not JsonObject;
  }

  /// <summary>
  /// Read-only check that the series and its requested season (or every real season when
  /// <paramref name="season"/> is <c>null</c>) are already monitored. Used to confirm that a monitoring
  /// change actually stuck — a fresh Sonarr add processes monitoring asynchronously and can briefly
  /// override it. Mirrors <see cref="EnsureSeasonsMonitored"/> without mutating.
  /// </summary>
  /// <param name="series">The full series resource fetched from Sonarr.</param>
  /// <param name="season">The season expected to be monitored, or <c>null</c> for all real seasons.</param>
  /// <returns><c>true</c> when the series and the target season(s) are monitored.</returns>
  public static bool AreSeasonsMonitored(JsonObject series, int? season)
  {
    ArgumentNullException.ThrowIfNull(series);

    if (series["monitored"] is not JsonValue rootValue || !rootValue.TryGetValue<bool>(out var rootMonitored) || !rootMonitored)
    {
      return false;
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

        var monitored = seasonObj["monitored"] is JsonValue sv && sv.TryGetValue<bool>(out var b) && b;
        if (!monitored)
        {
          return false;
        }
      }
    }

    return true;
  }

  /// <summary>
  /// Mutates an existing Sonarr series body so the given season is <b>unmonitored</b> (so Sonarr won't
  /// re-grab it after its files are deleted). Leaves the series and other seasons untouched. Returns
  /// <c>true</c> if the season's flag changed.
  /// </summary>
  /// <param name="series">The full series resource fetched from Sonarr.</param>
  /// <param name="season">The season to unmonitor.</param>
  /// <returns><c>true</c> when the season's monitored flag was changed.</returns>
  public static bool UnmonitorSeason(JsonObject series, int season)
  {
    ArgumentNullException.ThrowIfNull(series);
    if (series["seasons"] is not JsonArray seasons)
    {
      return false;
    }

    foreach (var node in seasons)
    {
      if (node is JsonObject seasonObj
          && seasonObj["seasonNumber"] is JsonValue numberValue
          && numberValue.TryGetValue<int>(out var number)
          && number == season)
      {
        var wasMonitored = seasonObj["monitored"] is not JsonValue sv || !sv.TryGetValue<bool>(out var b) || b;
        seasonObj["monitored"] = false;
        return wasMonitored;
      }
    }

    return false;
  }
}
