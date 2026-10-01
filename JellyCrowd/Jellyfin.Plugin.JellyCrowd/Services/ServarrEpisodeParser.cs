using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure (network-free) parsing of a Sonarr <c>/episode?seriesId=</c> payload, to pick out the episode
/// ids and episode-file ids of a given season (optionally a single episode) for targeted deletion.
/// </summary>
public static class ServarrEpisodeParser
{
  /// <summary>
  /// Selects the episode ids and distinct episode-file ids for a season (and optionally one episode).
  /// </summary>
  /// <param name="json">The raw Sonarr episodes JSON array.</param>
  /// <param name="season">The season number to match.</param>
  /// <param name="episode">The episode number to match, or <c>null</c> for the whole season.</param>
  /// <returns>The matching episode ids and (non-zero, deduped) episode-file ids.</returns>
  public static (IReadOnlyList<int> EpisodeIds, IReadOnlyList<int> FileIds) Select(string json, int season, int? episode)
  {
    ArgumentNullException.ThrowIfNull(json);
    var episodeIds = new List<int>();
    var fileIds = new List<int>();
    var seenFiles = new HashSet<int>();

    using var doc = JsonDocument.Parse(json);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
    {
      return (episodeIds, fileIds);
    }

    foreach (var el in doc.RootElement.EnumerateArray())
    {
      if (el.ValueKind != JsonValueKind.Object
          || !TryGetInt(el, "seasonNumber", out var s)
          || s != season)
      {
        continue;
      }

      if (episode is not null && (!TryGetInt(el, "episodeNumber", out var e) || e != episode.Value))
      {
        continue;
      }

      if (TryGetInt(el, "id", out var id))
      {
        episodeIds.Add(id);
      }

      if (TryGetInt(el, "episodeFileId", out var fileId) && fileId > 0 && seenFiles.Add(fileId))
      {
        fileIds.Add(fileId);
      }
    }

    return (episodeIds, fileIds);
  }

  /// <summary>
  /// Lists the episode ids to (re)monitor for a request: one episode, one season's episodes, or — for a
  /// whole-series request — every real episode (season 0 / specials excluded). A previous deletion unmonitors
  /// episodes so Sonarr won't immediately re-grab them; re-requesting must re-monitor them, or the season
  /// search skips the unmonitored ones and the deleted episodes never come back. A single-episode request
  /// re-monitors that episode only: re-monitoring its whole season would bring back the episodes deleted
  /// from it.
  /// </summary>
  /// <param name="json">The raw Sonarr episodes JSON array.</param>
  /// <param name="season">The season number, or <c>null</c> for the whole series.</param>
  /// <param name="episode">The episode number within <paramref name="season"/>, or <c>null</c> for the whole season.</param>
  /// <returns>The episode ids to monitor.</returns>
  public static IReadOnlyList<int> EpisodeIdsToMonitor(string json, int? season, int? episode = null)
  {
    ArgumentNullException.ThrowIfNull(json);
    var ids = new List<int>();

    using var doc = JsonDocument.Parse(json);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
    {
      return ids;
    }

    foreach (var el in doc.RootElement.EnumerateArray())
    {
      if (el.ValueKind != JsonValueKind.Object || !TryGetInt(el, "seasonNumber", out var s))
      {
        continue;
      }

      var wanted = season is int number ? s == number : s > 0;
      if (wanted && season is not null && episode is int only)
      {
        wanted = TryGetInt(el, "episodeNumber", out var e) && e == only;
      }

      if (wanted && TryGetInt(el, "id", out var id))
      {
        ids.Add(id);
      }
    }

    return ids;
  }

  /// <summary>
  /// Parses a Sonarr episode list.
  /// </summary>
  /// <param name="json">The raw Sonarr episodes JSON array.</param>
  /// <returns>Every episode with its id, numbers, monitored flag and file id (0 when it has no file).</returns>
  public static IReadOnlyList<SonarrEpisode> ParseEpisodes(string json)
  {
    ArgumentNullException.ThrowIfNull(json);
    var list = new List<SonarrEpisode>();
    using var doc = JsonDocument.Parse(json);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
    {
      return list;
    }

    foreach (var el in doc.RootElement.EnumerateArray())
    {
      if (el.ValueKind == JsonValueKind.Object
          && TryGetInt(el, "id", out var id)
          && TryGetInt(el, "seasonNumber", out var season)
          && TryGetInt(el, "episodeNumber", out var number))
      {
        var monitored = el.TryGetProperty("monitored", out var m) && m.ValueKind == JsonValueKind.True;
        var fileId = TryGetInt(el, "episodeFileId", out var f) && f > 0 ? f : 0;
        list.Add(new SonarrEpisode(id, season, number, monitored, fileId));
      }
    }

    return list;
  }

  /// <summary>
  /// Finds one episode in a Sonarr episode list: its id and whether it is monitored.
  /// </summary>
  /// <param name="json">The raw Sonarr episodes JSON array.</param>
  /// <param name="season">The season number.</param>
  /// <param name="episode">The episode number within the season.</param>
  /// <returns>The episode's id and monitored flag, or <c>null</c> when Sonarr does not list it (yet).</returns>
  public static (int Id, bool Monitored)? FindEpisode(string json, int season, int episode)
  {
    ArgumentNullException.ThrowIfNull(json);
    using var doc = JsonDocument.Parse(json);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
    {
      return null;
    }

    foreach (var el in doc.RootElement.EnumerateArray())
    {
      if (el.ValueKind == JsonValueKind.Object
          && TryGetInt(el, "seasonNumber", out var s) && s == season
          && TryGetInt(el, "episodeNumber", out var e) && e == episode
          && TryGetInt(el, "id", out var id))
      {
        var monitored = el.TryGetProperty("monitored", out var m) && m.ValueKind == JsonValueKind.True;
        return (id, monitored);
      }
    }

    return null;
  }

  private static bool TryGetInt(JsonElement parent, string property, out int value)
  {
    value = 0;
    return parent.TryGetProperty(property, out var el)
      && el.ValueKind == JsonValueKind.Number
      && el.TryGetInt32(out value);
  }
}
