using System;
using System.Globalization;
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
  private readonly IServarrClient _servarr;
  private readonly ITmdbClient _tmdb;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="ServarrDownloadClient"/> class.
  /// </summary>
  /// <param name="servarr">The Radarr/Sonarr client.</param>
  /// <param name="tmdb">The TMDB client (TMDB-&gt;TVDB resolution for shows).</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  public ServarrDownloadClient(IServarrClient servarr, ITmdbClient tmdb, Func<PluginConfiguration> config)
  {
    _servarr = servarr;
    _tmdb = tmdb;
    _config = config;
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
      if (movie?["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var movieId) && movieId > 0)
      {
        // Already in Radarr — just (re)search it instead of re-adding (which would 400).
        var command = new JsonObject { ["name"] = "MoviesSearch", ["movieIds"] = new JsonArray(movieId) };
        await _servarr.CommandAsync(config.RadarrUrl, config.RadarrApiKey, command, cancellationToken).ConfigureAwait(false);
        return;
      }

      var lookup = await _servarr.LookupMovieAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Radarr could not find TMDB movie {dispatch.TmdbId.ToString(CultureInfo.InvariantCulture)}.");
      var body = ServarrPayload.BuildMovieAdd(lookup, config.RadarrQualityProfileId, config.RadarrRootFolderPath);
      await _servarr.AddMovieAsync(config.RadarrUrl, config.RadarrApiKey, body, cancellationToken).ConfigureAwait(false);
    }
    else if (string.Equals(dispatch.MediaType, "tv", StringComparison.Ordinal))
    {
      if (!SonarrConfigured(config))
      {
        throw new InvalidOperationException("Sonarr is not configured (URL, API key, root folder and quality profile are required).");
      }

      var tvdbId = await _tmdb.GetTvdbIdAsync(dispatch.TmdbId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Could not resolve a TVDB id for TMDB show {dispatch.TmdbId.ToString(CultureInfo.InvariantCulture)}.");
      var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false);
      if (series is not null && series["id"] is JsonValue seriesIdValue && seriesIdValue.TryGetValue<int>(out var seriesId) && seriesId > 0)
      {
        // Already in Sonarr — but a series added for an earlier season leaves later seasons
        // UNMONITORED, and a season search on an unmonitored season grabs nothing. So monitor the
        // requested season first (persist the change), then search it (or the whole series).
        if (ServarrPayload.EnsureSeasonsMonitored(series, dispatch.Season))
        {
          await _servarr.UpdateSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, series, cancellationToken).ConfigureAwait(false);
        }

        var command = dispatch.Season is int season
          ? new JsonObject { ["name"] = "SeasonSearch", ["seriesId"] = seriesId, ["seasonNumber"] = season }
          : new JsonObject { ["name"] = "SeriesSearch", ["seriesId"] = seriesId };
        await _servarr.CommandAsync(config.SonarrUrl, config.SonarrApiKey, command, cancellationToken).ConfigureAwait(false);
        return;
      }

      var lookup = await _servarr.LookupSeriesAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Sonarr could not find TVDB series {tvdbId.ToString(CultureInfo.InvariantCulture)}.");
      var body = ServarrPayload.BuildSeriesAdd(lookup, config.SonarrQualityProfileId, config.SonarrLanguageProfileId, config.SonarrRootFolderPath, dispatch.Season);
      await _servarr.AddSeriesAsync(config.SonarrUrl, config.SonarrApiKey, body, cancellationToken).ConfigureAwait(false);
    }
    else
    {
      throw new InvalidOperationException($"Unsupported media type '{dispatch.MediaType}'.");
    }
  }

  /// <inheritdoc />
  public async Task CancelAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    var config = _config();

    // v1: only movies are undone (delete from Radarr stops its search/download). Deleting a whole
    // Sonarr series for a single-season request would be too destructive, so shows are left in place.
    if (!string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal) || !RadarrConfigured(config))
    {
      return;
    }

    // First remove any active download from the download client — deleting the Radarr movie alone
    // leaves the grab running (e.g. the torrent/RDT job keeps going). Best-effort; never blocks the delete.
    try
    {
      var queueJson = await _servarr.GetQueueAsync(config.RadarrUrl, config.RadarrApiKey, forSonarr: false, cancellationToken).ConfigureAwait(false);
      foreach (var queueId in ServarrQueueParser.ParseMovieQueueRecordIds(queueJson, dispatch.TmdbId))
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

    var movie = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
    if (movie?["id"] is System.Text.Json.Nodes.JsonValue idValue && idValue.TryGetValue<int>(out var movieId) && movieId > 0)
    {
      await _servarr.DeleteMovieAsync(config.RadarrUrl, config.RadarrApiKey, movieId, deleteFiles: true, cancellationToken).ConfigureAwait(false);
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
