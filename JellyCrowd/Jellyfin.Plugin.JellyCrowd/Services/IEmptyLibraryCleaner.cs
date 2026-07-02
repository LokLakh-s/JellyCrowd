using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Removes empty series (0 episodes) left behind in the Jellyfin library after their files were deleted.
/// </summary>
public interface IEmptyLibraryCleaner
{
  /// <summary>
  /// Sweeps the library for series that have no episodes and removes them (item + empty folder), skipping
  /// any that are newer than <paramref name="minAgeHours"/> or still wanted by an active request. See
  /// <see cref="EmptySeriesPolicy"/> for the exact rule. Never throws.
  /// </summary>
  /// <param name="minAgeHours">Minimum age, in hours, before an empty series is eligible for removal.</param>
  /// <param name="wantedTmdbIds">TMDB ids that an active (Pending/Approved) request still wants.</param>
  /// <returns>The number of empty series removed.</returns>
  int RemoveEmptySeries(int minAgeHours, IReadOnlySet<int> wantedTmdbIds);
}
