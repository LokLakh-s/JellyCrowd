using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="ISeriesStructureProvider"/>: Sonarr is the authority when it is the download
/// backend (its TVDB numbering is what gets downloaded), TMDB otherwise — and on any Sonarr failure.
/// </summary>
public sealed class SeriesStructureProvider : ISeriesStructureProvider
{
  private readonly ITmdbClient _tmdb;
  private readonly IServarrClient _servarr;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<SeriesStructureProvider> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="SeriesStructureProvider"/> class.
  /// </summary>
  /// <param name="tmdb">The TMDB client.</param>
  /// <param name="servarr">The Sonarr/Radarr client.</param>
  /// <param name="config">The plugin configuration accessor.</param>
  /// <param name="logger">The logger.</param>
  public SeriesStructureProvider(
    ITmdbClient tmdb,
    IServarrClient servarr,
    Func<PluginConfiguration> config,
    ILogger<SeriesStructureProvider> logger)
  {
    _tmdb = tmdb;
    _servarr = servarr;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<Season>> GetSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken)
  {
    var tmdbSeasons = await _tmdb.GetSeasonsAsync(tmdbId, language, cancellationToken).ConfigureAwait(false);
    var config = _config();
    if (!SonarrUsable(config))
    {
      return Offerable(tmdbSeasons);
    }

    try
    {
      var series = await ResolveSeriesAsync(config, tmdbId, cancellationToken).ConfigureAwait(false);
      if (series is null)
      {
        return Offerable(tmdbSeasons);
      }

      var seasons = ServarrSeriesParser.ParseSeasons(series);
      if (seasons.Count == 0)
      {
        return Offerable(tmdbSeasons);
      }

      // Borrow TMDB's localized names wherever the two numbering schemes agree; the client renders a
      // localized "Season N" for the rest (the very seasons TMDB does not know about).
      foreach (var season in seasons)
      {
        var named = tmdbSeasons.FirstOrDefault(t => t.SeasonNumber == season.SeasonNumber);
        if (named is not null)
        {
          season.Name = named.Name;
        }
      }

      return Offerable(seasons);
    }
    catch (Exception ex) when (IsTransient(ex))
    {
      _logger.LogWarning(ex, "Sonarr could not resolve the seasons of TMDB show {TmdbId}; using TMDB's.", tmdbId);
      return Offerable(tmdbSeasons);
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(int tmdbId, int season, string language, CancellationToken cancellationToken)
  {
    var config = _config();
    if (!SonarrUsable(config))
    {
      return await _tmdb.GetSeasonEpisodesAsync(tmdbId, season, language, cancellationToken).ConfigureAwait(false);
    }

    try
    {
      var tvdbId = await _tmdb.GetTvdbIdAsync(tmdbId, cancellationToken).ConfigureAwait(false);
      if (tvdbId is null)
      {
        return await _tmdb.GetSeasonEpisodesAsync(tmdbId, season, language, cancellationToken).ConfigureAwait(false);
      }

      // A show Sonarr already tracks: its episode list IS what gets downloaded, so use it verbatim.
      var added = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false);
      if (added is not null && ServarrSeriesParser.ParseSeriesId(added) is int seriesId)
      {
        var json = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
        return ServarrSeriesParser.ParseEpisodes(json, season);
      }

      // Not tracked yet: TMDB's episodes are only safe if both sources split the show the same way.
      var lookup = await _servarr.LookupSeriesAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false);
      if (lookup is null)
      {
        return await _tmdb.GetSeasonEpisodesAsync(tmdbId, season, language, cancellationToken).ConfigureAwait(false);
      }

      var sonarrNumbers = Numbers(ServarrSeriesParser.ParseSeasons(lookup));
      var tmdbNumbers = Numbers(await _tmdb.GetSeasonsAsync(tmdbId, language, cancellationToken).ConfigureAwait(false));
      if (sonarrNumbers.SetEquals(tmdbNumbers))
      {
        return await _tmdb.GetSeasonEpisodesAsync(tmdbId, season, language, cancellationToken).ConfigureAwait(false);
      }

      // The two disagree (an anime TMDB collapsed into one season, say). TMDB's episode numbers would
      // dispatch episodes Sonarr cannot honour, so offer none and let the user take the whole season.
      _logger.LogInformation(
        "TMDB and Sonarr disagree on the seasons of TMDB show {TmdbId}; offering season {Season} as a whole rather than TMDB's episodes.",
        tmdbId,
        season);
      return Array.Empty<Episode>();
    }
    catch (Exception ex) when (IsTransient(ex))
    {
      _logger.LogWarning(ex, "Sonarr could not resolve the episodes of TMDB show {TmdbId}; using TMDB's.", tmdbId);
      return await _tmdb.GetSeasonEpisodesAsync(tmdbId, season, language, cancellationToken).ConfigureAwait(false);
    }
  }

  // Sonarr is only consulted when it is the configured backend and reachable; reading seasons needs no
  // root folder or quality profile (unlike adding a series), so those are not required here.
  private static bool SonarrUsable(PluginConfiguration config)
    => string.Equals(config.DownloadBackend, "servarr", StringComparison.OrdinalIgnoreCase)
       && !string.IsNullOrWhiteSpace(config.SonarrUrl)
       && !string.IsNullOrWhiteSpace(config.SonarrApiKey);

  // An added series carries episode counts; a lookup (not tracked yet) still carries the right numbers.
  private async Task<JsonObject?> ResolveSeriesAsync(PluginConfiguration config, int tmdbId, CancellationToken cancellationToken)
  {
    var tvdbId = await _tmdb.GetTvdbIdAsync(tmdbId, cancellationToken).ConfigureAwait(false);
    if (tvdbId is null)
    {
      return null;
    }

    return await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false)
      ?? await _servarr.LookupSeriesAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false);
  }

  // Drop seasons known to be empty (TMDB lists announced seasons with no episode yet). A null count is
  // "unknown", not "empty", so it stays.
  private static List<Season> Offerable(IReadOnlyList<Season> seasons)
    => seasons.Where(s => s.EpisodeCount is not 0).ToList();

  private static HashSet<int> Numbers(IReadOnlyList<Season> seasons)
    => seasons.Where(s => s.SeasonNumber > 0 && s.EpisodeCount is not 0).Select(s => s.SeasonNumber).ToHashSet();

  private static bool IsTransient(Exception ex)
    => ex is HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException;
}
