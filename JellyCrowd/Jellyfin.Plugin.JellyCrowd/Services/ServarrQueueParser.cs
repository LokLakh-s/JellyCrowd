using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
  /// Finds the Sonarr queue record ids of a series (by TVDB id) that touch a scope: the whole series, one
  /// season, or one episode. Used to drop a stalled grab, which is dead for every episode it carries.
  /// </summary>
  /// <param name="json">The raw Sonarr <c>/queue?includeSeries=true&amp;includeEpisode=true</c> payload.</param>
  /// <param name="tvdbId">The series TVDB id to match.</param>
  /// <param name="season">When set, only match queue records for that season's episodes.</param>
  /// <param name="episode">When set (with <paramref name="season"/>), only match that episode's records.</param>
  /// <returns>The matching queue record ids.</returns>
  public static IReadOnlyList<int> ParseSeriesQueueRecordIds(string json, int tvdbId, int? season = null, int? episode = null)
  {
    ArgumentNullException.ThrowIfNull(json);
    var ids = new List<int>();
    foreach (var record in SeriesRecords(json, tvdbId))
    {
      if (InScope(record, season, episode))
      {
        ids.Add(record.Id);
      }
    }

    return ids;
  }

  /// <summary>
  /// Finds the downloads of a series (by TVDB id) that lie entirely within a scope — the whole series, one
  /// season, or one episode — as one queue record id per download (removing a record from the client
  /// removes its whole download). A download that also carries episodes outside the scope, such as a pack
  /// spanning seasons, is left alone: other requests may be waiting for those episodes.
  /// </summary>
  /// <param name="json">The raw Sonarr <c>/queue?includeSeries=true&amp;includeEpisode=true</c> payload.</param>
  /// <param name="tvdbId">The series TVDB id to match.</param>
  /// <param name="season">The season of the scope, or <c>null</c> for the whole series.</param>
  /// <param name="episode">The episode of the scope (with <paramref name="season"/>), or <c>null</c> for the whole season.</param>
  /// <returns>One queue record id per download within the scope.</returns>
  public static IReadOnlyList<int> ParseSeriesDownloadsWithin(string json, int tvdbId, int? season, int? episode)
  {
    ArgumentNullException.ThrowIfNull(json);
    return DownloadsWhere(json, tvdbId, r => InScope(r, season, episode));
  }

  /// <summary>
  /// Finds the downloads of a series (by TVDB id) that carry nothing but the given episodes, as one queue
  /// record id per download. A download that also carries another episode is left alone.
  /// </summary>
  /// <param name="json">The raw Sonarr <c>/queue?includeSeries=true&amp;includeEpisode=true</c> payload.</param>
  /// <param name="tvdbId">The series TVDB id to match.</param>
  /// <param name="episodes">The episodes being withdrawn.</param>
  /// <returns>One queue record id per download made only of those episodes.</returns>
  public static IReadOnlyList<int> ParseSeriesDownloadsWithin(string json, int tvdbId, IReadOnlySet<EpisodeKey> episodes)
  {
    ArgumentNullException.ThrowIfNull(json);
    ArgumentNullException.ThrowIfNull(episodes);
    return DownloadsWhere(json, tvdbId, r => r.Season is int s && r.Episode is int e && episodes.Contains(new EpisodeKey(s, e)));
  }

  private static List<int> DownloadsWhere(string json, int tvdbId, Func<SeriesRecord, bool> withdrawn)
  {
    var ids = new List<int>();
    foreach (var download in SeriesRecords(json, tvdbId).GroupBy(r => r.DownloadId ?? "#" + r.Id.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal))
    {
      if (download.All(withdrawn))
      {
        ids.Add(download.First().Id);
      }
    }

    return ids;
  }

  private static List<SeriesRecord> SeriesRecords(string json, int tvdbId)
  {
    var list = new List<SeriesRecord>();
    using var doc = JsonDocument.Parse(json);
    foreach (var record in Records(doc))
    {
      if (!record.TryGetProperty("series", out var series)
          || series.ValueKind != JsonValueKind.Object
          || !TryGetInt(series, "tvdbId", out var t)
          || t != tvdbId
          || !TryGetInt(record, "id", out var recordId))
      {
        continue;
      }

      int? season = null;
      int? number = null;
      if (record.TryGetProperty("episode", out var episode) && episode.ValueKind == JsonValueKind.Object)
      {
        season = TryGetInt(episode, "seasonNumber", out var s) ? s : null;
        number = TryGetInt(episode, "episodeNumber", out var e) ? e : null;
      }

      var downloadId = GetString(record, "downloadId");
      list.Add(new SeriesRecord(recordId, string.IsNullOrEmpty(downloadId) ? null : downloadId, season, number));
    }

    return list;
  }

  // A record with no episode details can't be placed, so it only matches the whole-series scope.
  private static bool InScope(SeriesRecord record, int? season, int? episode)
    => season is null
      || (record.Season == season && (episode is null || record.Episode == episode));

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

  private sealed record SeriesRecord(int Id, string? DownloadId, int? Season, int? Episode);
}
