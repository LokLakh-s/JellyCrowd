using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// What the automatic next-season request did for one played episode.
/// </summary>
public enum NextSeasonOutcome
{
  /// <summary>The administrator does not offer the feature.</summary>
  Disabled,

  /// <summary>The user has not opted in.</summary>
  NotOptedIn,

  /// <summary>Not near the end of the season yet, or there is no next season.</summary>
  NotYet,

  /// <summary>The next season was already dealt with for this user.</summary>
  AlreadyHandled,

  /// <summary>The next season is already in the library (there is nothing to fetch).</summary>
  AlreadyInLibrary,

  /// <summary>The next season was requested.</summary>
  Requested,

  /// <summary>The normal rules refused it (already on its way, quota, limits, parental restriction…).</summary>
  Refused,

  /// <summary>A check could not be made right now; it will be tried again on a later episode.</summary>
  Unavailable,
}

/// <summary>
/// Requests the next season of a show for a user who nears the end of the current one, when the
/// administrator offers it and the user opted in. Goes through <see cref="IRequestCreationService"/>, so
/// the request follows the normal path; each season is dealt with once per user.
/// </summary>
public interface INextSeasonRequester
{
  /// <summary>
  /// Considers the next season after a user started an episode.
  /// </summary>
  /// <param name="userId">The user who started playback.</param>
  /// <param name="seriesTmdbId">The show's TMDB id.</param>
  /// <param name="seriesName">The show's name, as the library shows it.</param>
  /// <param name="season">The season of the episode.</param>
  /// <param name="episode">The episode number.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>What was done.</returns>
  Task<NextSeasonOutcome> ConsiderAsync(Guid userId, int seriesTmdbId, string seriesName, int season, int episode, CancellationToken cancellationToken);
}
