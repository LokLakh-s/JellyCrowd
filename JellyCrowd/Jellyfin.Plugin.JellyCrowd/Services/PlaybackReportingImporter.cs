using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using JfEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Imports historical playback from the Playback Reporting plugin's SQLite database
/// (<c>playback_reporting.db</c> in the Jellyfin data folder) into the playback-history store. Only data
/// older than Jelly Crowd's own earliest record is imported, so the two captures never double-count; the
/// import is therefore idempotent. Episode series/season/number and viewer names are resolved at import time.
/// </summary>
public sealed class PlaybackReportingImporter : IPlaybackReportingImporter
{
  private const string DbFileName = "playback_reporting.db";
  private static readonly TimeSpan Retention = TimeSpan.FromDays(365);

  private readonly IPlaybackHistoryStore _store;
  private readonly IServerApplicationPaths _appPaths;
  private readonly ILibraryManager _libraryManager;
  private readonly Func<Guid, string> _resolveUserName;
  private readonly ILogger<PlaybackReportingImporter> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="PlaybackReportingImporter"/> class.
  /// </summary>
  /// <param name="store">The playback-history store.</param>
  /// <param name="appPaths">The server application paths (locates the data folder).</param>
  /// <param name="libraryManager">The library manager (resolves episode series/season/number).</param>
  /// <param name="resolveUserName">Resolves a user id to a display name.</param>
  /// <param name="logger">The logger.</param>
  public PlaybackReportingImporter(
    IPlaybackHistoryStore store,
    IServerApplicationPaths appPaths,
    ILibraryManager libraryManager,
    Func<Guid, string> resolveUserName,
    ILogger<PlaybackReportingImporter> logger)
  {
    _store = store;
    _appPaths = appPaths;
    _libraryManager = libraryManager;
    _resolveUserName = resolveUserName;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<ImportResultDto> ImportAsync(CancellationToken cancellationToken)
  {
    var dbPath = Path.Combine(_appPaths.DataPath, DbFileName);
    if (!File.Exists(dbPath))
    {
      return new ImportResultDto { Found = false };
    }

    var existing = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var cutoffUtc = existing.Count > 0 ? existing.Min(r => r.PlayedAtUtc) : DateTime.MaxValue;
    var retentionFrom = DateTime.UtcNow - Retention;
    var seen = new HashSet<string>(existing.Select(KeyOf), StringComparer.Ordinal);

    var toAdd = new List<PlaybackRecord>();
    var scanned = 0;

    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = dbPath,
      Mode = SqliteOpenMode.ReadOnly
    }.ConnectionString;

    var connection = new SqliteConnection(connectionString);
    await using (connection.ConfigureAwait(false))
    {
      await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
      using var command = connection.CreateCommand();
      command.CommandText =
        "SELECT DateCreated, UserId, ItemId, ItemType, ItemName, ClientName, PlayDuration FROM PlaybackActivity";

      var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
      await using (reader.ConfigureAwait(false))
      {
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
          scanned++;
          var record = MapRow(reader);
          if (record is null
              || record.PlayedAtUtc >= cutoffUtc
              || record.PlayedAtUtc < retentionFrom)
          {
            continue;
          }

          if (seen.Add(KeyOf(record)))
          {
            toAdd.Add(record);
          }
        }
      }
    }

    Enrich(toAdd);
    var imported = toAdd.Count > 0
      ? await _store.AddRangeAsync(toAdd, cancellationToken).ConfigureAwait(false)
      : 0;
    _logger.LogInformation(
      "Jelly Crowd: imported {Imported} of {Scanned} Playback Reporting records.", imported, scanned);
    return new ImportResultDto { Found = true, Scanned = scanned, Imported = imported };
  }

  // A stable per-play key (user · item · minute) used to skip duplicates against existing records.
  private static string KeyOf(PlaybackRecord record)
    => record.UserId.ToString("N", CultureInfo.InvariantCulture) + '|' + record.ItemId + '|'
       + record.PlayedAtUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

  private static PlaybackRecord? MapRow(SqliteDataReader reader)
  {
    DateTime playedAt;
    try
    {
      playedAt = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
    }
#pragma warning disable CA1031 // A single malformed row must not abort the whole import.
    catch (Exception)
#pragma warning restore CA1031
    {
      return null;
    }

    var userText = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
    if (!Guid.TryParse(userText, out var userId))
    {
      return null;
    }

    var itemIdRaw = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
    var itemId = Guid.TryParse(itemIdRaw, out var itemGuid)
      ? itemGuid.ToString("N", CultureInfo.InvariantCulture)
      : itemIdRaw;
    var seconds = reader.IsDBNull(6) ? 0L : reader.GetInt64(6);

    return new PlaybackRecord
    {
      UserId = userId,
      ItemId = itemId,
      ItemType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
      ItemName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
      Client = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
      PlayedAtUtc = playedAt,
      Minutes = Math.Round(seconds / 60.0, 2)
    };
  }

  // Fill in viewer names (not stored by Playback Reporting) and, for episodes whose item still exists,
  // the series/season/number so they group under "top shows" like live-captured plays.
  private void Enrich(List<PlaybackRecord> records)
  {
    var nameCache = new Dictionary<Guid, string>();
    foreach (var record in records)
    {
      if (!nameCache.TryGetValue(record.UserId, out var name))
      {
        name = ResolveName(record.UserId);
        nameCache[record.UserId] = name;
      }

      record.UserName = name;

      if (string.Equals(record.ItemType, "Episode", StringComparison.OrdinalIgnoreCase)
          && Guid.TryParse(record.ItemId, out var itemGuid)
          && _libraryManager.GetItemById(itemGuid) is JfEpisode episode)
      {
        record.SeriesName = episode.SeriesName ?? string.Empty;
        record.SeriesId = episode.SeriesId.ToString("N", CultureInfo.InvariantCulture);
        record.Season = episode.ParentIndexNumber;
        record.Episode = episode.IndexNumber;
      }
    }
  }

  private string ResolveName(Guid userId)
  {
    try
    {
      return _resolveUserName(userId) ?? string.Empty;
    }
#pragma warning disable CA1031 // Name resolution is best-effort.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Jelly Crowd: could not resolve a viewer name during import.");
      return string.Empty;
    }
  }
}
