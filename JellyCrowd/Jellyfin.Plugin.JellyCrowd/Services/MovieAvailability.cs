using System;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// When a requested movie can actually be found: its home release, by the rule Radarr applies to the
/// "released" availability every movie Jelly Crowd adds is given. Before it, Radarr will not take a
/// release, so searching is pointless and calling the movie "not found" is wrong.
/// </summary>
public static class MovieAvailability
{
  /// <summary>
  /// How long after its cinema release Radarr deems a movie out when no home release is listed.
  /// </summary>
  public static readonly TimeSpan CinemaToHomeDelay = TimeSpan.FromDays(90);

  /// <summary>
  /// Gets the movie's home release: its earliest digital or physical release, else its cinema release plus
  /// <see cref="CinemaToHomeDelay"/>, else unknown.
  /// </summary>
  /// <param name="release">The movie's release dates.</param>
  /// <returns>The UTC date, or <c>null</c> when TMDB lists none.</returns>
  public static DateTime? HomeRelease(MovieRelease release)
  {
    ArgumentNullException.ThrowIfNull(release);
    if (release.Digital is { } digital && release.Physical is { } physical)
    {
      return digital < physical ? digital : physical;
    }

    return release.Digital ?? release.Physical ?? release.Theatrical?.Add(CinemaToHomeDelay);
  }

  /// <summary>
  /// Determines whether the movie is not out and has no release date at all yet (announced, in
  /// production…): it cannot be found until one is set, however long it is searched for.
  /// </summary>
  /// <param name="release">The movie's release dates.</param>
  /// <returns><c>true</c> when TMDB says it is not released and lists no date.</returns>
  public static bool IsUnannounced(MovieRelease release)
  {
    ArgumentNullException.ThrowIfNull(release);
    return HomeRelease(release) is null
      && !string.IsNullOrWhiteSpace(release.Status)
      && !string.Equals(release.Status, "Released", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Works out a movie request's new desired time from its home release: that date while it is ahead,
  /// otherwise now at the latest (a date moved earlier makes it due at once, one already passed leaves the
  /// request as it is).
  /// </summary>
  /// <param name="request">The movie request.</param>
  /// <param name="release">The movie's release dates.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <returns>The new desired UTC time, or <c>null</c> when it is unknown or unchanged.</returns>
  public static DateTime? Reschedule(RequestRecord request, MovieRelease release, DateTime nowUtc)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (HomeRelease(release) is not { } home)
    {
      return null;
    }

    DateTime target;
    if (home > nowUtc)
    {
      target = home;
    }
    else if (request.DesiredAt is { } desired && desired <= nowUtc)
    {
      return null;
    }
    else
    {
      target = nowUtc;
    }

    return request.DesiredAt == target ? null : target;
  }
}
