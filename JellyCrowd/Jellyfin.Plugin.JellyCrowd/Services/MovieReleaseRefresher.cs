using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IMovieReleaseRefresher"/>. Reads release dates through the cached TMDB client, so a
/// sweep costs at most one TMDB call per movie per cache lifetime.
/// </summary>
public sealed class MovieReleaseRefresher : IMovieReleaseRefresher
{
  private readonly IRequestStore _store;
  private readonly ITmdbClient _tmdbClient;
  private readonly ILogger<MovieReleaseRefresher> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="MovieReleaseRefresher"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="tmdbClient">The TMDB client (release dates).</param>
  /// <param name="logger">The logger.</param>
  public MovieReleaseRefresher(IRequestStore store, ITmdbClient tmdbClient, ILogger<MovieReleaseRefresher> logger)
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
        && string.Equals(r.MediaType, "movie", StringComparison.Ordinal)
        && r.DeletionRequestedAt is null)
      .ToList();

    var now = DateTime.UtcNow;
    var rescheduled = 0;
    foreach (var movie in waiting.GroupBy(r => r.TmdbId))
    {
      cancellationToken.ThrowIfCancellationRequested();

      var release = await GetReleaseAsync(movie.Key, cancellationToken).ConfigureAwait(false);
      if (release is null)
      {
        continue;
      }

      var unannounced = MovieAvailability.IsUnannounced(release);
      foreach (var request in movie)
      {
        var desiredAt = MovieAvailability.Reschedule(request, release, now);
        if (desiredAt is null && unannounced == request.AwaitingReleaseDate)
        {
          continue;
        }

        await _store.ScheduleReleaseAsync(request.Id, desiredAt, unannounced, cancellationToken).ConfigureAwait(false);
        rescheduled++;
        _logger.LogInformation(
          "Request {RequestId} ({Title}) now expects its home release on {Date}.",
          request.Id.ToString("N", CultureInfo.InvariantCulture),
          request.Title,
          unannounced ? "an unannounced date" : (desiredAt ?? request.DesiredAt)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
      }
    }

    return rescheduled;
  }

  // The movie's release dates, or null when TMDB cannot be reached right now.
  private async Task<MovieRelease?> GetReleaseAsync(int tmdbId, CancellationToken cancellationToken)
  {
    try
    {
      return await _tmdbClient.GetMovieReleaseAsync(tmdbId, cancellationToken).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // A TMDB failure only delays the refresh to the next sweep.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not read the release dates of TMDB movie {TmdbId}.", tmdbId);
      return null;
    }
  }
}
