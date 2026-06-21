using System;
using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure (network-free) parsing of a Radarr/Sonarr <c>/queue</c> payload into download progress,
/// keyed so it can be matched back to a request (movies by TMDB id, episodes by TVDB id + season).
/// </summary>
public static class ServarrQueueParser
{
  /// <summary>
  /// Parses a Radarr queue payload into a map of TMDB id to progress. When several records share a
  /// TMDB id, the first (most relevant) one wins.
  /// </summary>
  /// <param name="json">The raw <c>/queue?includeMovie=true</c> payload.</param>
  /// <returns>A map of TMDB id to progress.</returns>
  public static IReadOnlyDictionary<int, QueueProgress> ParseMovieQueue(string json)
  {
    ArgumentNullException.ThrowIfNull(json);
    var map = new Dictionary<int, QueueProgress>();
    using var doc = JsonDocument.Parse(json);
    foreach (var record in Records(doc))
    {
      if (record.TryGetProperty("movie", out var movie)
          && movie.ValueKind == JsonValueKind.Object
          && TryGetInt(movie, "tmdbId", out var tmdbId)
          && !map.ContainsKey(tmdbId))
      {
        map[tmdbId] = BuildProgress(record);
      }
    }

    return map;
  }

  /// <summary>
  /// Finds the Radarr queue record ids for a given TMDB movie, so the active download can be removed
  /// from the download client on cancel (deleting the movie alone leaves the grab running, e.g. in RDT).
  /// </summary>
  /// <param name="json">The raw Radarr <c>/queue?includeMovie=true</c> payload.</param>
  /// <param name="tmdbId">The TMDB movie id to match.</param>
  /// <returns>The matching queue record ids.</returns>
  public static IReadOnlyList<int> ParseMovieQueueRecordIds(string json, int tmdbId)
  {
    ArgumentNullException.ThrowIfNull(json);
    var ids = new List<int>();
    using var doc = JsonDocument.Parse(json);
    foreach (var record in Records(doc))
    {
      if (record.TryGetProperty("movie", out var movie)
          && movie.ValueKind == JsonValueKind.Object
          && TryGetInt(movie, "tmdbId", out var t)
          && t == tmdbId
          && TryGetInt(record, "id", out var recordId))
      {
        ids.Add(recordId);
      }
    }

    return ids;
  }

  /// <summary>
  /// Parses a Sonarr queue payload into per-episode progress items carrying their series TVDB id.
  /// </summary>
  /// <param name="json">The raw <c>/queue?includeSeries=true&amp;includeEpisode=true</c> payload.</param>
  /// <returns>The queue items.</returns>
  public static IReadOnlyList<SeriesQueueItem> ParseSeriesQueue(string json)
  {
    ArgumentNullException.ThrowIfNull(json);
    var list = new List<SeriesQueueItem>();
    using var doc = JsonDocument.Parse(json);
    foreach (var record in Records(doc))
    {
      if (!record.TryGetProperty("series", out var series)
          || series.ValueKind != JsonValueKind.Object
          || !TryGetInt(series, "tvdbId", out var tvdbId))
      {
        continue;
      }

      var item = new SeriesQueueItem { TvdbId = tvdbId, Progress = BuildProgress(record) };
      if (record.TryGetProperty("episode", out var episode) && episode.ValueKind == JsonValueKind.Object)
      {
        if (TryGetInt(episode, "seasonNumber", out var season))
        {
          item.Season = season;
        }

        if (TryGetInt(episode, "episodeNumber", out var number))
        {
          item.Episode = number;
        }
      }

      list.Add(item);
    }

    return list;
  }

  private static IEnumerable<JsonElement> Records(JsonDocument doc)
  {
    var root = doc.RootElement;
    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var records))
    {
      root = records;
    }

    if (root.ValueKind != JsonValueKind.Array)
    {
      yield break;
    }

    foreach (var element in root.EnumerateArray())
    {
      if (element.ValueKind == JsonValueKind.Object)
      {
        yield return element;
      }
    }
  }

  private static QueueProgress BuildProgress(JsonElement record)
  {
    var size = GetDouble(record, "size");
    var sizeLeft = GetDouble(record, "sizeleft");
    var percent = size > 0 ? ((size - sizeLeft) / size) * 100 : 0;
    if (percent < 0)
    {
      percent = 0;
    }
    else if (percent > 100)
    {
      percent = 100;
    }

    var timeLeft = GetString(record, "timeleft");
    return new QueueProgress
    {
      State = MapState(GetString(record, "status"), GetString(record, "trackedDownloadState"), GetString(record, "trackedDownloadStatus")),
      Percent = Math.Round(percent),
      TimeLeft = string.IsNullOrWhiteSpace(timeLeft) ? null : timeLeft,
      SizeBytes = size > 0 ? (long)size : 0
    };
  }

  // Collapse Radarr/Sonarr's status + trackedDownloadState/Status into a handful of UI states.
  private static string MapState(string status, string trackedState, string trackedStatus)
  {
    if (Eq(trackedStatus, "warning") || Eq(trackedStatus, "error"))
    {
      return "warning";
    }

    if (Eq(trackedState, "importing") || Eq(trackedState, "importPending"))
    {
      return "importing";
    }

    if (Eq(trackedState, "imported"))
    {
      return "completed";
    }

    if (Eq(status, "downloading"))
    {
      return "downloading";
    }

    if (Eq(status, "completed"))
    {
      return "importing";
    }

    return "queued";
  }

  private static bool Eq(string value, string other)
    => string.Equals(value, other, StringComparison.OrdinalIgnoreCase);

  private static bool TryGetInt(JsonElement parent, string property, out int value)
  {
    value = 0;
    return parent.TryGetProperty(property, out var el)
      && el.ValueKind == JsonValueKind.Number
      && el.TryGetInt32(out value);
  }

  private static double GetDouble(JsonElement parent, string property)
    => parent.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)
      ? d
      : 0;

  private static string GetString(JsonElement parent, string property)
    => parent.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
      ? el.GetString() ?? string.Empty
      : string.Empty;
}
