using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure (network-free) parsing of Sonarr series/episode payloads into the catalog's season and episode
/// models. Sonarr speaks TVDB numbering — the very numbering it downloads with — so when it is the
/// backend these are the seasons a user can actually get, whatever TMDB says.
/// </summary>
public static class ServarrSeriesParser
{
  /// <summary>
  /// Parses the seasons of a Sonarr series payload (<c>/series?tvdbId=</c> or <c>/series/lookup</c>).
  /// Episode counts only exist on an added series; a lookup leaves them <c>null</c> (unknown, not zero).
  /// </summary>
  /// <param name="series">The Sonarr series object.</param>
  /// <returns>The seasons, without names (the caller supplies localized ones).</returns>
  public static IReadOnlyList<Season> ParseSeasons(JsonObject series)
  {
    ArgumentNullException.ThrowIfNull(series);

    var seasons = new List<Season>();
    if (series["seasons"] is not JsonArray array)
    {
      return seasons;
    }

    foreach (var node in array)
    {
      if (node is not JsonObject element || TryInt(element, "seasonNumber") is not int number)
      {
        continue;
      }

      int? episodeCount = null;
      if (element["statistics"] is JsonObject statistics)
      {
        episodeCount = TryInt(statistics, "totalEpisodeCount");
      }

      seasons.Add(new Season { SeasonNumber = number, Name = string.Empty, EpisodeCount = episodeCount });
    }

    return seasons;
  }

  /// <summary>
  /// Gets the Sonarr id of an <em>added</em> series. A lookup result for a series Sonarr does not track
  /// yet carries no (or a zero) id, in which case this returns <c>null</c>.
  /// </summary>
  /// <param name="series">The Sonarr series object.</param>
  /// <returns>The series id, or <c>null</c> when the series is not added.</returns>
  public static int? ParseSeriesId(JsonObject series)
  {
    ArgumentNullException.ThrowIfNull(series);
    return TryInt(series, "id") is int id && id > 0 ? id : null;
  }

  /// <summary>
  /// Parses a Sonarr <c>/episode?seriesId=</c> payload into the episodes of a single season, ordered by
  /// episode number.
  /// </summary>
  /// <param name="json">The raw Sonarr episodes JSON array.</param>
  /// <param name="season">The season number to keep.</param>
  /// <returns>The season's episodes.</returns>
  public static IReadOnlyList<Episode> ParseEpisodes(string json, int season)
  {
    ArgumentNullException.ThrowIfNull(json);

    var episodes = new List<Episode>();
    using var doc = JsonDocument.Parse(json);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
    {
      return episodes;
    }

    foreach (var element in doc.RootElement.EnumerateArray())
    {
      if (element.ValueKind != JsonValueKind.Object
          || GetInt(element, "seasonNumber") != season
          || GetInt(element, "episodeNumber") is not int number)
      {
        continue;
      }

      episodes.Add(new Episode
      {
        SeasonNumber = season,
        EpisodeNumber = number,
        Name = GetString(element, "title") ?? string.Empty,
        AirDate = GetString(element, "airDate")
      });
    }

    episodes.Sort((a, b) => a.EpisodeNumber.CompareTo(b.EpisodeNumber));
    return episodes;
  }

  private static int? TryInt(JsonObject element, string property)
    => element[property] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

  private static int? GetInt(JsonElement element, string property)
    => element.TryGetProperty(property, out var value)
       && value.ValueKind == JsonValueKind.Number
       && value.TryGetInt32(out var number)
      ? number
      : null;

  private static string? GetString(JsonElement element, string property)
    => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
      ? value.GetString()
      : null;
}
