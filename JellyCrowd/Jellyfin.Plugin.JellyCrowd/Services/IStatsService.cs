using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Builds the statistics shown on the admin dashboard from captured playback history and the library.
/// </summary>
public interface IStatsService
{
  /// <summary>
  /// Builds the statistics overview for the given rolling window (in days; 0 = all time).
  /// </summary>
  /// <param name="windowDays">The window size in days (0 = all time).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The overview.</returns>
  Task<StatsOverviewDto> GetOverviewAsync(int windowDays, CancellationToken cancellationToken);

  /// <summary>
  /// Builds the personal dashboard for one user: their viewing statistics plus request and quota activity.
  /// </summary>
  /// <param name="userId">The user.</param>
  /// <param name="windowDays">The viewing window in days (0 = all time).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The user's dashboard.</returns>
  Task<UserDashboardDto> GetUserDashboardAsync(Guid userId, int windowDays, CancellationToken cancellationToken);
}
