using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IEpisodeAirDateRefresher"/>. Reads episode lists through the cached TMDB client, so a
/// sweep costs at most one TMDB call per season per cache lifetime.
/// </summary>
public sealed class EpisodeAirDateRefresher : IEpisodeAirDateRefresher
{
  // Same language as the reconciler's episode lookups, so both share one cached episode list.
  private const string EpisodeListLanguage = "en-US";

  private readonly IRequestStore _store;
  private readonly ITmdbClient _tmdbClient;
  private readonly ILogger<EpisodeAirDateRefresher> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="EpisodeAirDateRefresher"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="tmdbClient">The TMDB client (episode lists).</param>
  /// <param name="logger">The logger.</param>
  public EpisodeAirDateRefresher(IRequestStore store, ITmdbClient tmdbClient, ILogger<EpisodeAirDateRefresher> logger)
  {
    _store = store;
    _tmdbClient = tmdbClient;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<int> RefreshAsync(CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var waiting = all.Where(r =>
        r.Status is RequestStatus.Pending or RequestStatus.Approved
        && string.Equals(r.MediaType, "tv", StringComparison.Ordinal)
        && r.Season is not null
        && r.Episode is not null
        && r.DeletionRequestedAt is null)
      .ToList();

    var now = DateTime.UtcNow;
    var rescheduled = 0;
    foreach (var season in waiting.GroupBy(r => (r.TmdbId, Season: r.Season!.Value)))
    {
      cancellationToken.ThrowIfCancellationRequested();

      var airDates = await GetAirDatesAsync(season.Key.TmdbId, season.Key.Season, cancellationToken).ConfigureAwait(false);
      if (airDates is null)
      {
        continue;
      }

      foreach (var request in season)
      {
        var airDate = airDates.GetValueOrDefault(request.Episode!.Value);
        if (airDate is null || RequestScheduling.RealignOnReleaseDate(request, airDate, now) is not { } desiredAt)
        {
          continue;
        }

        var previous = request.ReleaseDate ?? "unknown";
        await _store.RescheduleAsync(request.Id, airDate, desiredAt, cancellationToken).ConfigureAwait(false);
        rescheduled++;
        _logger.LogInformation(
          "Request {RequestId} ({Title} S{Season}E{Episode}) now airs on {AirDate} (was {Previous}).",
          request.Id.ToString("N", CultureInfo.InvariantCulture),
          request.Title,
          season.Key.Season,
          request.Episode,
          airDate,
          previous);
      }
    }

    return rescheduled;
  }

  // Air date by episode number for one season, or null when TMDB cannot be reached right now.
  private async Task<Dictionary<int, string?>?> GetAirDatesAsync(int tmdbId, int season, CancellationToken cancellationToken)
  {
    try
    {
      var episodes = await _tmdbClient.GetSeasonEpisodesAsync(tmdbId, season, EpisodeListLanguage, cancellationToken).ConfigureAwait(false);
      var dates = new Dictionary<int, string?>();
      foreach (var episode in episodes)
      {
        dates.TryAdd(episode.EpisodeNumber, episode.AirDate);
      }

      return dates;
    }
#pragma warning disable CA1031 // A TMDB failure only delays the refresh to the next sweep.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not list the episodes of TMDB show {TmdbId} season {Season}.", tmdbId, season);
      return null;
    }
  }
}
