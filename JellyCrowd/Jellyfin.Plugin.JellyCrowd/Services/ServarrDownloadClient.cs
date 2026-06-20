using System;
using System.Globalization;
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
  public async Task DispatchAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    var config = _config();

    if (string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal))
    {
      if (!RadarrConfigured(config))
      {
        throw new InvalidOperationException("Radarr is not configured (URL, API key, root folder and quality profile are required).");
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
