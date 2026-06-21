using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IServarrStatusService"/>: queries the Radarr and/or Sonarr queue once per call
/// and matches entries back to approved requests (movies by TMDB id, episodes by TVDB id + season).
/// Requests not in the queue are classified as missing/unreleased from the item itself, cached
/// briefly so the fast UI poll does not hammer the *arr instances.
/// </summary>
public sealed class ServarrStatusService : IServarrStatusService
{
  private static readonly TimeSpan ItemStateTtl = TimeSpan.FromSeconds(60);

  private readonly IServarrClient _servarr;
  private readonly ITmdbClient _tmdb;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<ServarrStatusService> _logger;
  private readonly ConcurrentDictionary<string, (DateTime At, string? State)> _itemStateCache = new(StringComparer.Ordinal);

  /// <summary>
  /// Initializes a new instance of the <see cref="ServarrStatusService"/> class.
  /// </summary>
  /// <param name="servarr">The Radarr/Sonarr client.</param>
  /// <param name="tmdb">The TMDB client (TMDB-&gt;TVDB resolution for shows).</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public ServarrStatusService(IServarrClient servarr, ITmdbClient tmdb, Func<PluginConfiguration> config, ILogger<ServarrStatusService> logger)
  {
    _servarr = servarr;
    _tmdb = tmdb;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<DownloadStatusDto>> GetStatusesAsync(IEnumerable<RequestRecord> requests, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(requests);
    var config = _config();
    var result = new List<DownloadStatusDto>();

    if (!string.Equals(config.DownloadBackend, "servarr", StringComparison.Ordinal))
    {
      return result;
    }

    // Only approved-but-not-yet-available requests can be mid-download.
    var pending = requests.Where(r => r.Status == RequestStatus.Approved).ToList();
    if (pending.Count == 0)
    {
      return result;
    }

    var movies = pending.Where(r => string.Equals(r.MediaType, "movie", StringComparison.Ordinal)).ToList();
    if (movies.Count > 0 && RadarrConfigured(config))
    {
      await AddMovieStatusesAsync(config, movies, result, cancellationToken).ConfigureAwait(false);
    }

    var shows = pending.Where(r => string.Equals(r.MediaType, "tv", StringComparison.Ordinal)).ToList();
    if (shows.Count > 0 && SonarrConfigured(config))
    {
      await AddSeriesStatusesAsync(config, shows, result, cancellationToken).ConfigureAwait(false);
    }

    // Requests with no queue entry: report missing/unreleased/completed from the item itself.
    var covered = new HashSet<Guid>(result.Select(r => r.RequestId));
    foreach (var request in pending.Where(r => !covered.Contains(r.Id)))
    {
      var state = await GetItemStateAsync(config, request, cancellationToken).ConfigureAwait(false);
      if (state is not null)
      {
        result.Add(new DownloadStatusDto { RequestId = request.Id, State = state, Percent = 0 });
      }
    }

    return result;
  }

  private async Task<string?> GetItemStateAsync(PluginConfiguration config, RequestRecord request, CancellationToken cancellationToken)
  {
    var key = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{request.MediaType}:{request.TmdbId}:{request.Season}:{request.Episode}");
    if (_itemStateCache.TryGetValue(key, out var cached) && (DateTime.UtcNow - cached.At) < ItemStateTtl)
    {
      return cached.State;
    }

    string? state = null;
    try
    {
      if (string.Equals(request.MediaType, "movie", StringComparison.Ordinal) && RadarrConfigured(config))
      {
        var movie = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, request.TmdbId, cancellationToken).ConfigureAwait(false);
        state = ServarrItemState.Movie(movie);
      }
      else if (string.Equals(request.MediaType, "tv", StringComparison.Ordinal) && SonarrConfigured(config))
      {
        state = await ResolveTvStateAsync(config, request, cancellationToken).ConfigureAwait(false);
      }
    }
#pragma warning disable CA1031 // Best-effort: a failed lookup just yields no extra badge.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not resolve the {Type} item state for TMDB {TmdbId}.", request.MediaType, request.TmdbId);
    }

    _itemStateCache[key] = (DateTime.UtcNow, state);
    return state;
  }

  private async Task<string?> ResolveTvStateAsync(PluginConfiguration config, RequestRecord request, CancellationToken cancellationToken)
  {
    var tvdbId = await _tmdb.GetTvdbIdAsync(request.TmdbId, cancellationToken).ConfigureAwait(false);
    if (tvdbId is not { } tvdb)
    {
      return null;
    }

    var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdb, cancellationToken).ConfigureAwait(false);
    if (series?["id"] is not JsonValue idValue || !idValue.TryGetValue<int>(out var seriesId) || seriesId <= 0)
    {
      return null;
    }

    var episodesJson = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
    return ServarrItemState.Episodes(episodesJson, request.Season, request.Episode, DateTime.UtcNow);
  }

  private async Task AddMovieStatusesAsync(PluginConfiguration config, List<RequestRecord> movies, List<DownloadStatusDto> result, CancellationToken cancellationToken)
  {
    try
    {
      var json = await _servarr.GetQueueAsync(config.RadarrUrl, config.RadarrApiKey, forSonarr: false, cancellationToken).ConfigureAwait(false);
      var byTmdb = ServarrQueueParser.ParseMovieQueue(json);
      foreach (var request in movies)
      {
        if (byTmdb.TryGetValue(request.TmdbId, out var progress))
        {
          result.Add(ToDto(request.Id, progress));
        }
      }
    }
#pragma warning disable CA1031 // Download status is best-effort; a queue failure must not break the listing.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not read the Radarr queue.");
    }
  }

  private async Task AddSeriesStatusesAsync(PluginConfiguration config, List<RequestRecord> shows, List<DownloadStatusDto> result, CancellationToken cancellationToken)
  {
    try
    {
      var json = await _servarr.GetQueueAsync(config.SonarrUrl, config.SonarrApiKey, forSonarr: true, cancellationToken).ConfigureAwait(false);
      var items = ServarrQueueParser.ParseSeriesQueue(json);
      if (items.Count == 0)
      {
        return;
      }

      // Resolve TMDB->TVDB once per distinct show.
      var tvdbByTmdb = new Dictionary<int, int?>();
      foreach (var request in shows)
      {
        if (!tvdbByTmdb.TryGetValue(request.TmdbId, out var tvdbId))
        {
          tvdbId = await _tmdb.GetTvdbIdAsync(request.TmdbId, cancellationToken).ConfigureAwait(false);
          tvdbByTmdb[request.TmdbId] = tvdbId;
        }

        if (tvdbId is not { } tvdb)
        {
          continue;
        }

        var matches = items.Where(i => i.TvdbId == tvdb
          && (request.Season is null || i.Season == request.Season)
          && (request.Episode is null || i.Episode == request.Episode)).ToList();
        if (matches.Count > 0)
        {
          result.Add(ToDto(request.Id, Aggregate(matches)));
        }
      }
    }
#pragma warning disable CA1031 // Download status is best-effort; a queue failure must not break the listing.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not read the Sonarr queue.");
    }
  }

  // A season request can map to several episodes downloading at once: report the least-advanced one
  // (the season isn't "done" until its slowest episode is), surfacing warnings first.
  private static QueueProgress Aggregate(IReadOnlyList<SeriesQueueItem> matches)
  {
    var warning = matches.FirstOrDefault(m => string.Equals(m.Progress.State, "warning", StringComparison.Ordinal));
    if (warning is not null)
    {
      return warning.Progress;
    }

    return matches.OrderBy(m => m.Progress.Percent).First().Progress;
  }

  private static DownloadStatusDto ToDto(Guid requestId, QueueProgress progress) => new DownloadStatusDto
  {
    RequestId = requestId,
    State = progress.State,
    Percent = progress.Percent,
    TimeLeft = progress.TimeLeft,
    SizeBytes = progress.SizeBytes
  };

  private static bool RadarrConfigured(PluginConfiguration config)
    => !string.IsNullOrWhiteSpace(config.RadarrUrl) && !string.IsNullOrWhiteSpace(config.RadarrApiKey);

  private static bool SonarrConfigured(PluginConfiguration config)
    => !string.IsNullOrWhiteSpace(config.SonarrUrl) && !string.IsNullOrWhiteSpace(config.SonarrApiKey);
}
