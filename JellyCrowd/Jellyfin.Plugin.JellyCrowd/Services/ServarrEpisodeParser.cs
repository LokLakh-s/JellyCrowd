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

  private static bool TryGetInt(JsonElement parent, string property, out int value)
  {
    value = 0;
    return parent.TryGetProperty(property, out var el)
      && el.ValueKind == JsonValueKind.Number
      && el.TryGetInt32(out value);
  }
}
