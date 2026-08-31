using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Download backend that adds approved requests to Radarr (movies) or Sonarr (shows) and triggers a
/// search. Shows are resolved from TMDB to TVDB first, since Sonarr is TVDB-based. Jelly Crowd only
/// registers the item; Radarr/Sonarr perform the actual search and download.
/// </summary>
public sealed class ServarrDownloadClient : IDownloadClient
{
  // A fresh Sonarr add processes monitoring asynchronously: because we add inert (monitor: none), Sonarr
  // unmonitors everything a moment after the add returns, which would clobber a single up-front monitor
  // call and leave the series unmonitored (nothing searchable). After applying monitoring we therefore
  // wait, re-check, and re-apply until Sonarr reports it monitored — bounded by these constants.
  private const int MonitorConfirmAttempts = 6;
  private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1);

  private readonly IServarrClient _servarr;
  private readonly ITmdbClient _tmdb;
  private readonly Func<PluginConfiguration> _config;
  private readonly Func<CancellationToken, Task> _settleDelay;

  /// <summary>
  /// Initializes a new instance of the <see cref="ServarrDownloadClient"/> class.
  /// </summary>
  /// <param name="servarr">The Radarr/Sonarr client.</param>
  /// <param name="tmdb">The TMDB client (TMDB-&gt;TVDB resolution for shows).</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="settleDelay">
  /// Delay awaited between monitor-confirm attempts, letting Sonarr's asynchronous post-add processing run.
  /// Defaults to a one-second real delay; tests inject a no-op.
  /// </param>
  public ServarrDownloadClient(IServarrClient servarr, ITmdbClient tmdb, Func<PluginConfiguration> config, Func<CancellationToken, Task>? settleDelay = null)
  {
    _servarr = servarr;
    _tmdb = tmdb;
    _config = config;
    _settleDelay = settleDelay ?? (ct => Task.Delay(SettleDelay, ct));
  }

  /// <inheritdoc />
  public string Backend => "servarr";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return RadarrConfigured(config) || SonarrConfigured(config);
  }

  /// <inheritdoc />
  public Task DispatchAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    return EnsureRequestedAsync(dispatch, cancellationToken);
  }

  /// <inheritdoc />
  public Task RetryAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    return EnsureRequestedAsync(dispatch, cancellationToken);
  }

  /// <inheritdoc />
  public async Task RescanAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    var config = _config();

    if (string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal))
    {
      if (!RadarrConfigured(config))
      {
        return;
      }

      var movie = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      if (movie?["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var movieId) && movieId > 0)
      {
        var command = new JsonObject { ["name"] = "RescanMovie", ["movieId"] = movieId };
        await _servarr.CommandAsync(config.RadarrUrl, config.RadarrApiKey, command, cancellationToken).ConfigureAwait(false);
      }
    }
    else if (string.Equals(dispatch.MediaType, "tv", StringComparison.Ordinal))
    {
      if (!SonarrConfigured(config))
      {
        return;
      }

      var tvdbId = await ResolveTvdbIdAsync(config, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      if (tvdbId is null)
      {
        return;
      }

      var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false);
      if (series is not null && series["id"] is JsonValue seriesIdValue && seriesIdValue.TryGetValue<int>(out var seriesId) && seriesId > 0)
      {
        var command = new JsonObject { ["name"] = "RescanSeries", ["seriesId"] = seriesId };
        await _servarr.CommandAsync(config.SonarrUrl, config.SonarrApiKey, command, cancellationToken).ConfigureAwait(false);
      }
    }
  }

  // Idempotent dispatch: if the title is already in Radarr/Sonarr, (re)trigger a search; otherwise add
  // it (the add payload already requests a search). Crucially, re-adding an existing title makes
  // Radarr/Sonarr return 400 — which previously failed the dispatch, left it "due", and made the
  // scheduled task re-add it forever (the "Blocked / 400 Bad Request" flapping). Checking first avoids that.
  private async Task EnsureRequestedAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    var config = _config();

    if (string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal))
    {
      if (!RadarrConfigured(config))
      {
        throw new InvalidOperationException("Radarr is not configured (URL, API key, root folder and quality profile are required).");
      }

      var movie = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      if (TryGetId(movie, out var movieId))
      {
        // Already in Radarr — just (re)search it instead of re-adding (which would 400).
        await SearchMovieAsync(config, movieId, cancellationToken).ConfigureAwait(false);
        return;
      }

      var lookup = await _servarr.LookupMovieAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Radarr could not find TMDB movie {dispatch.TmdbId.ToString(CultureInfo.InvariantCulture)}.");
      var body = ServarrPayload.BuildMovieAdd(lookup, config.RadarrQualityProfileId, config.RadarrRootFolderPath);
      try
      {
        await _servarr.AddMovieAsync(config.RadarrUrl, config.RadarrApiKey, body, cancellationToken).ConfigureAwait(false);
      }
      catch (HttpRequestException)
      {
        // A concurrent request (e.g. several titles grabbed at once) may have added it first, so this add
        // 400s ("movie already exists"). If it's there now, recover by searching it instead of failing the
        // dispatch — a failed dispatch would surface a spurious "Blocked" until reconciliation.
        var added = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
        if (!TryGetId(added, out var addedMovieId))
        {
          throw;
        }

        await SearchMovieAsync(config, addedMovieId, cancellationToken).ConfigureAwait(false);
      }
    }
    else if (string.Equals(dispatch.MediaType, "tv", StringComparison.Ordinal))
    {
      if (!SonarrConfigured(config))
      {
        throw new InvalidOperationException("Sonarr is not configured (URL, API key, root folder and quality profile are required).");
      }

      var tvdbId = await ResolveTvdbIdAsync(config, dispatch.TmdbId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Could not resolve a TVDB id for TMDB show {dispatch.TmdbId.ToString(CultureInfo.InvariantCulture)} (no TVDB or IMDb match).");
      var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false);
      if (series is not null && TryGetId(series, out var seriesId))
      {
        await SearchSeriesAsync(config, series, seriesId, dispatch, cancellationToken).ConfigureAwait(false);
        return;
      }

      var lookup = await _servarr.LookupSeriesAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Sonarr could not find TVDB series {tvdbId.ToString(CultureInfo.InvariantCulture)}.");
      var body = ServarrPayload.BuildSeriesAdd(lookup, config.SonarrQualityProfileId, config.SonarrLanguageProfileId, config.SonarrRootFolderPath, dispatch.Season);
      try
      {
        await _servarr.AddSeriesAsync(config.SonarrUrl, config.SonarrApiKey, body, cancellationToken).ConfigureAwait(false);
      }
      catch (HttpRequestException)
      {
        // A concurrent request (e.g. two seasons of the same show grabbed at once) may have added the
        // series first, so this add 400s ("series already added"). That is fine — it is in Sonarr now, and
        // the monitor + search below handles it. Any other add failure is surfaced by the fetch that follows.
      }

      // The series was added inert (nothing monitored, no search). Fetch it and monitor + search ONLY the
      // requested season — never rely on the add to grab, or Sonarr's default "monitor: all" pulls the
      // whole show for a single-season request.
      var added = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false);
      if (added is null || !TryGetId(added, out var addedSeriesId))
      {
        throw new InvalidOperationException($"Sonarr did not accept the series for TVDB {tvdbId.ToString(CultureInfo.InvariantCulture)}.");
      }

      // Fresh add: confirm the monitoring sticks against Sonarr's async post-add unmonitor before searching.
      await ConfirmMonitorThenSearchSeriesAsync(config, added, addedSeriesId, tvdbId, dispatch, cancellationToken).ConfigureAwait(false);
    }
    else
    {
      throw new InvalidOperationException($"Unsupported media type '{dispatch.MediaType}'.");
    }
  }

  // (Re)search a movie already present in Radarr.
  private Task SearchMovieAsync(PluginConfiguration config, int movieId, CancellationToken cancellationToken)
  {
    var command = new JsonObject { ["name"] = "MoviesSearch", ["movieIds"] = new JsonArray(movieId) };
    return _servarr.CommandAsync(config.RadarrUrl, config.RadarrApiKey, command, cancellationToken);
  }

  // Monitor the requested season (a series added for an earlier season leaves later seasons UNMONITORED,
  // and a search on an unmonitored season grabs nothing), persist that, then search the season (or series).
  // Used when the series is ALREADY in Sonarr: there is no async post-add processing to race, so a single
  // monitor call sticks.
  private async Task SearchSeriesAsync(PluginConfiguration config, JsonObject series, int seriesId, DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    if (ServarrPayload.EnsureSeasonsMonitored(series, dispatch.Season))
    {
      await _servarr.UpdateSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, series, cancellationToken).ConfigureAwait(false);
    }

    await MonitorEpisodesAndSearchAsync(config, seriesId, dispatch, cancellationToken).ConfigureAwait(false);
  }

  // Used right after a FRESH add: Sonarr processes the add asynchronously and — because we add inert
  // (monitor: none) — unmonitors the series and its episodes a moment later, which clobbers a single
  // up-front monitor call (the reported "series stays unmonitored, monitor/search greyed out" bug). So we
  // apply the monitoring, wait for Sonarr's post-add to run, re-check, and re-apply until it reports the
  // series monitored (bounded), and only then search — so the search actually has monitored episodes.
  private async Task ConfirmMonitorThenSearchSeriesAsync(PluginConfiguration config, JsonObject series, int seriesId, int tvdbId, DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    var current = series;
    for (var attempt = 0; attempt < MonitorConfirmAttempts; attempt++)
    {
      if (ServarrPayload.EnsureSeasonsMonitored(current, dispatch.Season))
      {
        await _servarr.UpdateSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, current, cancellationToken).ConfigureAwait(false);
      }

      await _settleDelay(cancellationToken).ConfigureAwait(false);

      var refreshed = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false);
      if (refreshed is null)
      {
        break; // can't verify — we already applied the monitoring; fall through to the search.
      }

      current = refreshed;
      if (ServarrPayload.AreSeasonsMonitored(current, dispatch.Season))
      {
        break; // Sonarr's post-add has run and our monitoring survived — stable.
      }
    }

    await MonitorEpisodesAndSearchAsync(config, seriesId, dispatch, cancellationToken).ConfigureAwait(false);
  }

  // Monitor the requested season's episodes (Sonarr's inert add and any prior deletion leave them
  // unmonitored one by one, and setting only the season flag does not reliably cascade back), then trigger
  // a targeted season search (or a whole-series search when no season was requested).
  private async Task MonitorEpisodesAndSearchAsync(PluginConfiguration config, int seriesId, DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    var episodesJson = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
    if (!string.IsNullOrEmpty(episodesJson))
    {
      var episodeIds = ServarrEpisodeParser.EpisodeIdsToMonitor(episodesJson, dispatch.Season);
      if (episodeIds.Count > 0)
      {
        await _servarr.SetEpisodesMonitoredAsync(config.SonarrUrl, config.SonarrApiKey, episodeIds, monitored: true, cancellationToken).ConfigureAwait(false);
      }
    }

    var command = dispatch.Season is int season
      ? new JsonObject { ["name"] = "SeasonSearch", ["seriesId"] = seriesId, ["seasonNumber"] = season }
      : new JsonObject { ["name"] = "SeriesSearch", ["seriesId"] = seriesId };
    await _servarr.CommandAsync(config.SonarrUrl, config.SonarrApiKey, command, cancellationToken).ConfigureAwait(false);
  }

  // Resolves the TVDB id Sonarr needs from a TMDB show id. Normally TMDB carries it, but some entries
  // (e.g. The Haunting of Hill House) have no TVDB id — for those we fall back to the IMDb id, which
  // Sonarr can look up, and whose result carries the TVDB id. Returns null when neither path resolves.
  private async Task<int?> ResolveTvdbIdAsync(PluginConfiguration config, int tmdbId, CancellationToken cancellationToken)
  {
    var tvdbId = await _tmdb.GetTvdbIdAsync(tmdbId, cancellationToken).ConfigureAwait(false);
    if (tvdbId is not null)
    {
      return tvdbId;
    }

    var details = await _tmdb.GetDetailsAsync("tv", tmdbId, "en-US", cancellationToken).ConfigureAwait(false);
    if (string.IsNullOrEmpty(details?.ImdbId))
    {
      return null;
    }

    var byImdb = await _servarr.LookupSeriesByImdbAsync(config.SonarrUrl, config.SonarrApiKey, details.ImdbId, cancellationToken).ConfigureAwait(false);
    return byImdb?["tvdbId"] is JsonValue v && v.TryGetValue<int>(out var resolved) && resolved > 0 ? resolved : null;
  }

  private static bool TryGetId(JsonObject? obj, out int id)
  {
    if (obj?["id"] is JsonValue value && value.TryGetValue<int>(out var parsed) && parsed > 0)
    {
      id = parsed;
      return true;
    }

    id = 0;
    return false;
  }

  /// <inheritdoc />
  public Task CancelAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    var config = _config();

    // A user cancelling a single request: only movies are undone (delete from Radarr stops its
    // search/download). Deleting a whole Sonarr series for a single-season cancel would be too
    // destructive, so shows are left in place. Full removal (incl. Sonarr) is PurgeAsync's job.
    if (!string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal) || !RadarrConfigured(config))
    {
      return Task.CompletedTask;
    }

    return RemoveMovieAsync(config, dispatch.TmdbId, cancellationToken);
  }

  /// <inheritdoc />
  public async Task<bool> PurgeAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    var config = _config();

    try
    {
      if (string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal))
      {
        if (RadarrConfigured(config))
        {
          await RemoveMovieAsync(config, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
        }

        return true; // removed, or Radarr not configured (nothing this backend put there).
      }

      if (!string.Equals(dispatch.MediaType, "tv", StringComparison.Ordinal) || !SonarrConfigured(config))
      {
        return true;
      }

      var tvdbId = await ResolveTvdbIdAsync(config, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      if (tvdbId is null)
      {
        return true; // can't resolve it → nothing actionable to purge.
      }

      var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false);
      if (series is null || series["id"] is not JsonValue idValue || !idValue.TryGetValue<int>(out var seriesId) || seriesId <= 0)
      {
        return true; // not in Sonarr → nothing to purge.
      }

      // Remove any active downloads for the series from the client (best-effort).
      await RemoveSeriesQueueAsync(config, tvdbId.Value, cancellationToken).ConfigureAwait(false);

      if (dispatch.Season is int season)
      {
        // Targeted purge: a single season (or one episode) — unmonitor it so Sonarr won't re-grab, then
        // delete just that season's/episode's files from Sonarr + disk. The whole series is left in place.
        var episodesJson = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
        var (episodeIds, fileIds) = ServarrEpisodeParser.Select(episodesJson, season, dispatch.Episode);

        if (dispatch.Episode is null)
        {
          if (ServarrPayload.UnmonitorSeason(series, season))
          {
            await _servarr.UpdateSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, series, cancellationToken).ConfigureAwait(false);
          }
        }
        else if (episodeIds.Count > 0)
        {
          await _servarr.SetEpisodesMonitoredAsync(config.SonarrUrl, config.SonarrApiKey, episodeIds, monitored: false, cancellationToken).ConfigureAwait(false);
        }

        foreach (var fileId in fileIds)
        {
          await _servarr.DeleteEpisodeFileAsync(config.SonarrUrl, config.SonarrApiKey, fileId, cancellationToken).ConfigureAwait(false);
        }

        return true;
      }

      // Whole-show request: remove the entire series (with its files).
      await _servarr.DeleteSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, deleteFiles: true, cancellationToken).ConfigureAwait(false);
      return true;
    }
#pragma warning disable CA1031 // A purge failure (e.g. backend down) is reported so the caller can retry.
    catch (Exception)
#pragma warning restore CA1031
    {
      return false;
    }
  }

  private async Task RemoveMovieAsync(PluginConfiguration config, int tmdbId, CancellationToken cancellationToken)
  {
    // First remove any active download from the download client — deleting the Radarr movie alone
    // leaves the grab running (e.g. the torrent/RDT job keeps going). Best-effort; never blocks the delete.
    try
    {
      var queueJson = await _servarr.GetQueueAsync(config.RadarrUrl, config.RadarrApiKey, forSonarr: false, cancellationToken).ConfigureAwait(false);
      foreach (var queueId in ServarrQueueParser.ParseMovieQueueRecordIds(queueJson, tmdbId))
      {
        await _servarr.DeleteQueueItemAsync(config.RadarrUrl, config.RadarrApiKey, queueId, removeFromClient: true, blocklist: false, cancellationToken).ConfigureAwait(false);
      }
    }
#pragma warning disable CA1031 // Queue cleanup is best-effort; still delete the movie below.
    catch (Exception)
#pragma warning restore CA1031
    {
      // Ignore — fall through to deleting the movie.
    }

    var movie = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, tmdbId, cancellationToken).ConfigureAwait(false);
    if (movie?["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var movieId) && movieId > 0)
    {
      await _servarr.DeleteMovieAsync(config.RadarrUrl, config.RadarrApiKey, movieId, deleteFiles: true, cancellationToken).ConfigureAwait(false);
    }
  }

  private async Task RemoveSeriesQueueAsync(PluginConfiguration config, int tvdbId, CancellationToken cancellationToken)
  {
    try
    {
      var queueJson = await _servarr.GetQueueAsync(config.SonarrUrl, config.SonarrApiKey, forSonarr: true, cancellationToken).ConfigureAwait(false);
      foreach (var queueId in ServarrQueueParser.ParseSeriesQueueRecordIds(queueJson, tvdbId))
      {
        await _servarr.DeleteQueueItemAsync(config.SonarrUrl, config.SonarrApiKey, queueId, removeFromClient: true, blocklist: false, cancellationToken).ConfigureAwait(false);
      }
    }
#pragma warning disable CA1031 // Queue cleanup is best-effort; the season/series removal still proceeds.
    catch (Exception)
#pragma warning restore CA1031
    {
      // Ignore — fall through to the deletion.
    }
  }

  /// <inheritdoc />
  public async Task TestAsync(CancellationToken cancellationToken)
  {
    var config = _config();
    var tested = false;

    if (RadarrConfigured(config))
    {
      await _servarr.TestAsync(config.RadarrUrl, config.RadarrApiKey, cancellationToken).ConfigureAwait(false);
      tested = true;
    }

    if (SonarrConfigured(config))
    {
      await _servarr.TestAsync(config.SonarrUrl, config.SonarrApiKey, cancellationToken).ConfigureAwait(false);
      tested = true;
    }

    if (!tested)
    {
      throw new InvalidOperationException("Configure Radarr and/or Sonarr (URL, API key, root folder and quality profile).");
    }
  }

  private static bool RadarrConfigured(PluginConfiguration config)
    => !string.IsNullOrWhiteSpace(config.RadarrUrl)
       && !string.IsNullOrWhiteSpace(config.RadarrApiKey)
       && !string.IsNullOrWhiteSpace(config.RadarrRootFolderPath)
       && config.RadarrQualityProfileId > 0;

  private static bool SonarrConfigured(PluginConfiguration config)
    => !string.IsNullOrWhiteSpace(config.SonarrUrl)
       && !string.IsNullOrWhiteSpace(config.SonarrApiKey)
       && !string.IsNullOrWhiteSpace(config.SonarrRootFolderPath)
       && config.SonarrQualityProfileId > 0;
}
