using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Radarr/Sonarr implementation of <see cref="IStalledDownloadRecovery"/>: when a grab sits without
/// progress past the configured threshold (e.g. a stalled torrent / dead debrid cache), it removes and
/// blocklists that release and triggers a fresh search so a different release is grabbed automatically.
/// </summary>
public sealed class ServarrStalledRecovery : IStalledDownloadRecovery
{
  private readonly IServarrClient _servarr;
  private readonly ITmdbClient _tmdb;
  private readonly IRequestStore _store;
  private readonly Func<PluginConfiguration> _config;
  private readonly ISeriesMappingStore? _mappings;
  private readonly ILogger<ServarrStalledRecovery> _logger;
  private readonly StallTracker _tracker = new();
  private readonly TitleOperationLock _titleLock;
  private readonly Func<DateTime> _clock;

  /// <summary>
  /// Initializes a new instance of the <see cref="ServarrStalledRecovery"/> class.
  /// </summary>
  /// <param name="servarr">The Radarr/Sonarr client.</param>
  /// <param name="tmdb">The TMDB client (TMDB→TVDB resolution for shows).</param>
  /// <param name="store">The request store.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  /// <param name="titleLock">Serializes the backend operations of one title (shared with the download dispatcher).</param>
  /// <param name="mappings">Where Sonarr files the TMDB shows TMDB splits otherwise, if any.</param>
  public ServarrStalledRecovery(IServarrClient servarr, ITmdbClient tmdb, IRequestStore store, Func<PluginConfiguration> config, ILogger<ServarrStalledRecovery> logger, TitleOperationLock titleLock, ISeriesMappingStore? mappings = null)
    : this(servarr, tmdb, store, config, logger, titleLock, () => DateTime.UtcNow, mappings)
  {
  }

  // Tests drive the clock, so a stall can be observed without waiting for the real threshold.
  internal ServarrStalledRecovery(IServarrClient servarr, ITmdbClient tmdb, IRequestStore store, Func<PluginConfiguration> config, ILogger<ServarrStalledRecovery> logger, TitleOperationLock titleLock, Func<DateTime> clock, ISeriesMappingStore? mappings = null)
  {
    _mappings = mappings;
    _servarr = servarr;
    _tmdb = tmdb;
    _store = store;
    _config = config;
    _logger = logger;
    _titleLock = titleLock;
    _clock = clock;
  }

  /// <inheritdoc />
  public async Task RecoverAsync(CancellationToken cancellationToken)
  {
    var config = _config();
    if (!config.RecoverStalledDownloads || !string.Equals(config.DownloadBackend, "servarr", StringComparison.Ordinal))
    {
      return;
    }

    var threshold = TimeSpan.FromMinutes(Math.Max(1, config.StalledRecoveryMinutes));
    var now = _clock();

    try
    {
      var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
      var approved = all.Where(r => r.Status == RequestStatus.Approved).ToList();
      if (approved.Count == 0)
      {
        return;
      }

      if (RadarrConfigured(config) && approved.Any(r => string.Equals(r.MediaType, "movie", StringComparison.Ordinal)))
      {
        await RecoverMoviesAsync(config, approved, now, threshold, cancellationToken).ConfigureAwait(false);
      }

      if (SonarrConfigured(config) && approved.Any(r => string.Equals(r.MediaType, "tv", StringComparison.Ordinal)))
      {
        await RecoverShowsAsync(config, approved, now, threshold, cancellationToken).ConfigureAwait(false);
      }
    }
#pragma warning disable CA1031 // A background sweep failure must not crash the scheduled task.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd stalled-download recovery failed.");
    }
  }

  private async Task RecoverMoviesAsync(PluginConfiguration config, IReadOnlyList<RequestRecord> approved, DateTime now, TimeSpan threshold, CancellationToken cancellationToken)
  {
    var queueJson = await _servarr.GetQueueAsync(config.RadarrUrl, config.RadarrApiKey, forSonarr: false, cancellationToken).ConfigureAwait(false);
    var queue = ServarrQueueParser.ParseMovieQueue(queueJson);

    foreach (var request in approved.Where(r => string.Equals(r.MediaType, "movie", StringComparison.Ordinal)))
    {
      var key = request.Id.ToString("N", CultureInfo.InvariantCulture);
      if (!queue.TryGetValue(request.TmdbId, out var progress))
      {
        _tracker.Forget(key); // not in the queue → nothing to watch
        continue;
      }

      if (!_tracker.ShouldRecover(key, progress.Percent, progress.State, now, threshold))
      {
        continue;
      }

      using var titleLock = await _titleLock.EnterAsync(request.MediaType, request.TmdbId, cancellationToken).ConfigureAwait(false);
      if (!await IsStillApprovedAsync(request, cancellationToken).ConfigureAwait(false))
      {
        continue;
      }

      // Blocklist + remove the stalled grab(s), then re-search for a different release.
      foreach (var queueId in ServarrQueueParser.ParseMovieQueueRecordIds(queueJson, request.TmdbId))
      {
        await _servarr.DeleteQueueItemAsync(config.RadarrUrl, config.RadarrApiKey, queueId, removeFromClient: true, blocklist: true, cancellationToken).ConfigureAwait(false);
      }

      var movie = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, request.TmdbId, cancellationToken).ConfigureAwait(false);
      if (movie?["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var movieId) && movieId > 0)
      {
        var command = new JsonObject { ["name"] = "MoviesSearch", ["movieIds"] = new JsonArray(movieId) };
        await _servarr.CommandAsync(config.RadarrUrl, config.RadarrApiKey, command, cancellationToken).ConfigureAwait(false);
      }

      _logger.LogInformation("Jelly Crowd: recovered stalled download for \"{Title}\" (blocklisted + re-searched).", request.Title);
    }
  }

  private async Task RecoverShowsAsync(PluginConfiguration config, IReadOnlyList<RequestRecord> approved, DateTime now, TimeSpan threshold, CancellationToken cancellationToken)
  {
    var queueJson = await _servarr.GetQueueAsync(config.SonarrUrl, config.SonarrApiKey, forSonarr: true, cancellationToken).ConfigureAwait(false);
    var queue = ServarrQueueParser.ParseSeriesQueue(queueJson);

    foreach (var request in approved.Where(r => string.Equals(r.MediaType, "tv", StringComparison.Ordinal)))
    {
      // A show Sonarr files under another series is looked for there, in Sonarr's season numbering.
      var mapping = _mappings?.Get(request.TmdbId);
      var tvdbId = mapping?.TvdbId ?? await _tmdb.GetTvdbIdAsync(request.TmdbId, cancellationToken).ConfigureAwait(false);
      var season = mapping is null ? request.Season : mapping.ToSonarrScope(request.Season);
      if (tvdbId is null || (mapping is not null && season is null))
      {
        continue;
      }

      // The request's own scope only: an episode request must not act on its season's other downloads.
      var match = queue.FirstOrDefault(q => q.TvdbId == tvdbId.Value
        && (season is null || q.Season == season)
        && (season is null || request.Episode is null || q.Episode == request.Episode));
      var key = request.Id.ToString("N", CultureInfo.InvariantCulture);
      if (match is null)
      {
        _tracker.Forget(key);
        continue;
      }

      if (!_tracker.ShouldRecover(key, match.Progress.Percent, match.Progress.State, now, threshold))
      {
        continue;
      }

      using var titleLock = await _titleLock.EnterAsync(request.MediaType, request.TmdbId, cancellationToken).ConfigureAwait(false);
      if (!await IsStillApprovedAsync(request, cancellationToken).ConfigureAwait(false))
      {
        continue;
      }

      foreach (var queueId in ServarrQueueParser.ParseSeriesQueueRecordIds(queueJson, tvdbId.Value, season, request.Episode))
      {
        await _servarr.DeleteQueueItemAsync(config.SonarrUrl, config.SonarrApiKey, queueId, removeFromClient: true, blocklist: true, cancellationToken).ConfigureAwait(false);
      }

      var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false);
      if (series?["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var seriesId) && seriesId > 0)
      {
        var command = await BuildSearchAsync(config, seriesId, season, request.Episode, cancellationToken).ConfigureAwait(false);
        if (command is not null)
        {
          await _servarr.CommandAsync(config.SonarrUrl, config.SonarrApiKey, command, cancellationToken).ConfigureAwait(false);
        }
      }

      _logger.LogInformation("Jelly Crowd: recovered stalled download for \"{Title}\" (blocklisted + re-searched).", request.Title);
    }
  }

  // A request cancelled since the sweep started must not be searched (and grabbed) again.
  private async Task<bool> IsStillApprovedAsync(RequestRecord request, CancellationToken cancellationToken)
    => (await _store.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false))?.Status == RequestStatus.Approved;

  // The re-search for a request's scope: its episode alone, its season, or the whole series. Null when the
  // episode is not listed in Sonarr.
  private async Task<JsonObject?> BuildSearchAsync(PluginConfiguration config, int seriesId, int? sonarrSeason, int? requestedEpisode, CancellationToken cancellationToken)
  {
    if (sonarrSeason is not int season)
    {
      return new JsonObject { ["name"] = "SeriesSearch", ["seriesId"] = seriesId };
    }

    if (requestedEpisode is not int episode)
    {
      return new JsonObject { ["name"] = "SeasonSearch", ["seriesId"] = seriesId, ["seasonNumber"] = season };
    }

    var episodesJson = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
    var listed = string.IsNullOrEmpty(episodesJson) ? null : ServarrEpisodeParser.FindEpisode(episodesJson, season, episode);
    return listed is { } found ? new JsonObject { ["name"] = "EpisodeSearch", ["episodeIds"] = new JsonArray(found.Id) } : null;
  }

  private static bool RadarrConfigured(PluginConfiguration config)
    => !string.IsNullOrWhiteSpace(config.RadarrUrl) && !string.IsNullOrWhiteSpace(config.RadarrApiKey);

  private static bool SonarrConfigured(PluginConfiguration config)
    => !string.IsNullOrWhiteSpace(config.SonarrUrl) && !string.IsNullOrWhiteSpace(config.SonarrApiKey);
}
