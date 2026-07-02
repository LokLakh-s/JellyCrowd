using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure decision for whether an empty series (0 episodes) should be removed from the library. A series
/// with no episodes is normally a ghost left behind after a deletion — Jellyfin keeps the series entry
/// even once every episode file is gone. Two guards prevent removing a series that is still wanted:
/// a minimum age (a just-added series may be mid-download, and a re-download always creates a fresh
/// series item) and an active-request check (a Pending/Approved request is still being fulfilled).
/// </summary>
public static class EmptySeriesPolicy
{
  /// <summary>
  /// Decides whether an empty series should be removed.
  /// </summary>
  /// <param name="episodeCount">The number of episodes currently under the series.</param>
  /// <param name="seriesAddedUtc">When the series was added to the library (<see cref="MediaBrowser.Controller.Entities.BaseItem.DateCreated"/>).</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="minAge">Minimum time the series must have existed before it can be removed.</param>
  /// <param name="seriesTmdbId">The series' TMDB id, or <c>null</c> when it has none.</param>
  /// <param name="wantedTmdbIds">TMDB ids of titles an active (Pending/Approved) request still wants.</param>
  /// <returns><c>true</c> when the series is an empty ghost safe to remove.</returns>
  public static bool ShouldRemove(int episodeCount, DateTime seriesAddedUtc, DateTime nowUtc, TimeSpan minAge, int? seriesTmdbId, IReadOnlySet<int> wantedTmdbIds)
  {
    ArgumentNullException.ThrowIfNull(wantedTmdbIds);

    if (episodeCount > 0)
    {
      return false; // still has media
    }

    if (nowUtc - seriesAddedUtc < minAge)
    {
      return false; // too fresh — may be mid-download
    }

    if (seriesTmdbId is int id && wantedTmdbIds.Contains(id))
    {
      return false; // an active request is still fulfilling it
    }

    return true;
  }
}
