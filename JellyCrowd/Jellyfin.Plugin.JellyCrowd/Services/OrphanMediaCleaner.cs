using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IOrphanMediaCleaner"/>. A media (a movie, or a season) becomes an orphan when no request
/// covers it any more — no owner, nothing pending: an expired ownership, the last owner taken away by an admin,
/// a season Sonarr fetched for nobody. It is deleted once it has stayed an orphan for the deletion retention,
/// which leaves time to give it to someone. The clock lives in memory: a restart only postpones a deletion.
/// </summary>
public sealed class OrphanMediaCleaner : IOrphanMediaCleaner
{
  // How long to keep retrying a failed backend purge before deleting from the library anyway (as the deletion
  // task does), so an unreachable backend cannot keep an orphan forever.
  private static readonly TimeSpan PurgeRetryGrace = TimeSpan.FromDays(7);

  private readonly IRequestStore _store;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly IMediaDeleter _mediaDeleter;
  private readonly IDownloadDispatcher _downloadDispatcher;
  private readonly IActivityLog _activityLog;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<OrphanMediaCleaner> _logger;
  private readonly Func<DateTime> _clock;
  // Only the deletion task calls this, and Jellyfin never runs a task twice at once: no locking needed.
  private readonly Dictionary<string, DateTime> _orphanSince = new(StringComparer.Ordinal);

  /// <summary>
  /// Initializes a new instance of the <see cref="OrphanMediaCleaner"/> class.
  /// </summary>
  /// <param name="store">The request store (who owns or wants what).</param>
  /// <param name="libraryMatcher">The library matcher (the library's movies and seasons).</param>
  /// <param name="mediaDeleter">Deletes library items.</param>
  /// <param name="downloadDispatcher">Purges the media from Radarr/Sonarr.</param>
  /// <param name="activityLog">The activity log.</param>
  /// <param name="config">Provides the plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public OrphanMediaCleaner(IRequestStore store, ILibraryMatcher libraryMatcher, IMediaDeleter mediaDeleter, IDownloadDispatcher downloadDispatcher, IActivityLog activityLog, Func<PluginConfiguration> config, ILogger<OrphanMediaCleaner> logger)
    : this(store, libraryMatcher, mediaDeleter, downloadDispatcher, activityLog, config, logger, () => DateTime.UtcNow)
  {
  }

  internal OrphanMediaCleaner(IRequestStore store, ILibraryMatcher libraryMatcher, IMediaDeleter mediaDeleter, IDownloadDispatcher downloadDispatcher, IActivityLog activityLog, Func<PluginConfiguration> config, ILogger<OrphanMediaCleaner> logger, Func<DateTime> clock)
  {
    _store = store;
    _libraryMatcher = libraryMatcher;
    _mediaDeleter = mediaDeleter;
    _downloadDispatcher = downloadDispatcher;
    _activityLog = activityLog;
    _config = config;
    _logger = logger;
    _clock = clock;
  }

  /// <inheritdoc />
  public async Task<int> CleanAsync(CancellationToken cancellationToken)
  {
    var config = _config();
    if (!config.DeleteOrphanMedia || config.OrphanCleanupLibraryIds.Count == 0)
    {
      _orphanSince.Clear(); // turned back on later, every orphan waits the full retention again
      return 0;
    }

    var now = _clock();
    var retention = TimeSpan.FromHours(Math.Max(0, config.DeletionRetentionHours));
    var inScope = _mediaDeleter.ItemsIn(config.OrphanCleanupLibraryIds);
    var requests = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var orphans = new HashSet<string>(StringComparer.Ordinal);
    var deleted = 0;

    foreach (var item in _libraryMatcher.ListLibraryMedia())
    {
      if (!Guid.TryParse(item.JellyfinItemId, out var id) || !inScope.Contains(id) || IsWanted(item, requests))
      {
        continue;
      }

      var key = Key(item);
      orphans.Add(key);
      if (!_orphanSince.TryGetValue(key, out var since))
      {
        _orphanSince[key] = since = now;
      }

      if (now - since < retention)
      {
        continue;
      }

      if (await DeleteAsync(item, now - since > retention + PurgeRetryGrace, cancellationToken).ConfigureAwait(false))
      {
        deleted++;
        orphans.Remove(key);
      }
    }

    // Owned again, requested again, or gone: the clock starts over if it ever becomes an orphan again.
    foreach (var key in _orphanSince.Keys.Where(k => !orphans.Contains(k)).ToList())
    {
      _orphanSince.Remove(key);
    }

    return deleted;
  }

  /// <summary>
  /// Whether a request still covers a media of the library: an owner (deletion requested or not — the deletion
  /// task deals with those), or a request on its way (a season still downloading must not be cleaned).
  /// </summary>
  /// <param name="item">The library media.</param>
  /// <param name="requests">Every request.</param>
  /// <returns><c>true</c> when it is not an orphan.</returns>
  internal static bool IsWanted(LibraryMediaItem item, IEnumerable<RequestRecord> requests)
    => requests.Any(r => r.Status != RequestStatus.Denied
      && r.TmdbId == item.TmdbId
      && string.Equals(r.MediaType, item.MediaType, StringComparison.Ordinal)
      && MediaScope.Overlaps(item.Season, null, r.Season, r.Episode));

  private static string Key(LibraryMediaItem item)
    => item.MediaType + ":" + item.TmdbId.ToString(CultureInfo.InvariantCulture) + ":" + (item.Season?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

  private static string Label(LibraryMediaItem item)
    => item.Season is int season ? item.Title + " S" + season.ToString(CultureInfo.InvariantCulture) : item.Title;

  // Purges the backend first (as the deletion task does): Radarr drops the movie, Sonarr stops monitoring the
  // season and deletes its files. Jellyfin then deletes the folder. A failed purge is retried next run.
  private async Task<bool> DeleteAsync(LibraryMediaItem item, bool purgeGraceElapsed, CancellationToken cancellationToken)
  {
    var libraryDeletes = _mediaDeleter.Exists(item.JellyfinItemId);
    var media = new RequestRecord
    {
      TmdbId = item.TmdbId,
      MediaType = item.MediaType,
      Title = item.Title,
      Season = item.Season,
      JellyfinItemId = item.JellyfinItemId
    };

    var purged = await _downloadDispatcher.PurgeAsync(media, libraryDeletes, cancellationToken).ConfigureAwait(false);
    if (!purged && !purgeGraceElapsed)
    {
      _logger.LogWarning("Jelly Crowd orphan cleanup: backend purge failed for {Title}; retrying next run.", Label(item));
      return false;
    }

    if (libraryDeletes)
    {
      _mediaDeleter.Delete(item.JellyfinItemId);
    }

    _logger.LogInformation("Jelly Crowd orphan cleanup: deleted {Title}, owned by nobody.", Label(item));
    _ = _activityLog.LogAsync("info", "admin", "Deleted " + Label(item) + ": owned by nobody", null, CancellationToken.None);
    return true;
  }
}
