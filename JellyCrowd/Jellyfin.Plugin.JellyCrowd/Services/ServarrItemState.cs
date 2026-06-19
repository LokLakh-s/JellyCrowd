using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure (network-free) classification of a Radarr movie / Sonarr episodes payload into a coarse
/// availability state for a request that is <b>not</b> currently downloading:
/// <c>completed</c> (file present), <c>missing</c> (released/aired but no file yet) or
/// <c>unreleased</c> (not out yet). Returns <c>null</c> when nothing meaningful can be said.
/// </summary>
public static class ServarrItemState
{
  /// <summary>
  /// Classifies a Radarr movie object (<c>hasFile</c> / <c>isAvailable</c>).
  /// </summary>
  /// <param name="movie">The Radarr movie object, or <c>null</c>.</param>
  /// <returns>The state, or <c>null</c> when the movie is unknown.</returns>
  public static string? Movie(JsonObject? movie)
  {
    if (movie is null)
    {
      return null;
    }

    if (GetBool(movie, "hasFile"))
    {
      return "completed";
    }

    return GetBool(movie, "isAvailable") ? "missing" : "unreleased";
  }

  /// <summary>
  /// Classifies a Sonarr episodes payload for a requested season (and optionally a single episode).
  /// </summary>
  /// <param name="episodesJson">The raw <c>/episode?seriesId=…</c> JSON array.</param>
  /// <param name="season">The requested season, or <c>null</c> for the whole series (specials excluded).</param>
  /// <param name="episode">The requested episode within the season, or <c>null</c> for the whole season.</param>
  /// <param name="nowUtc">The reference "now" used to decide whether an episode has aired.</param>
  /// <returns>The aggregated state, or <c>null</c> when nothing matches.</returns>
  public static string? Episodes(string episodesJson, int? season, int? episode, DateTime nowUtc)
  {
    ArgumentNullException.ThrowIfNull(episodesJson);
    if (JsonNode.Parse(episodesJson) is not JsonArray array)
    {
      return null;
    }

    var matched = array
      .OfType<JsonObject>()
      .Where(o => (season is null ? GetInt(o, "seasonNumber") > 0 : GetInt(o, "seasonNumber") == season)
        && (episode is null || GetInt(o, "episodeNumber") == episode))
      .ToList();

    if (matched.Count == 0)
    {
      return null;
    }

    if (matched.All(o => GetBool(o, "hasFile")))
    {
      return "completed";
    }

    // Episode-level requests look at that single episode; season-level only weighs monitored episodes.
    var requireMonitored = episode is null;
    var pending = matched
      .Where(o => !GetBool(o, "hasFile") && (!requireMonitored || GetBool(o, "monitored")))
      .ToList();

    if (pending.Count == 0)
    {
      return null;
    }

    return pending.Any(o => HasAired(o, nowUtc)) ? "missing" : "unreleased";
  }

  private static bool HasAired(JsonObject episode, DateTime nowUtc)
  {
    var air = episode["airDateUtc"]?.GetValue<string>();
    return !string.IsNullOrWhiteSpace(air)
      && DateTime.TryParse(air, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
      && parsed <= nowUtc;
  }

  private static bool GetBool(JsonObject obj, string property)
    => obj[property] is JsonValue value && value.TryGetValue<bool>(out var b) && b;

  private static int GetInt(JsonObject obj, string property)
    => obj[property] is JsonValue value && value.TryGetValue<int>(out var i) ? i : -1;
}
