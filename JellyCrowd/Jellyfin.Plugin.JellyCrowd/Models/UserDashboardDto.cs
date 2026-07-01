using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The personal dashboard for one user: their own Jellyfin viewing statistics plus their request activity
/// and disk-quota usage.
/// </summary>
public class UserDashboardDto
{
  /// <summary>Gets or sets the number of days the viewing figures cover (0 = all time).</summary>
  public int WindowDays { get; set; }

  /// <summary>Gets or sets the user's total plays in the window.</summary>
  public int TotalPlays { get; set; }

  /// <summary>Gets or sets the user's total watched minutes in the window.</summary>
  public double TotalMinutes { get; set; }

  /// <summary>Gets or sets the user's most-played movies.</summary>
  public IReadOnlyList<StatsItemDto> TopMovies { get; set; } = Array.Empty<StatsItemDto>();

  /// <summary>Gets or sets the user's most-played shows.</summary>
  public IReadOnlyList<StatsItemDto> TopShows { get; set; } = Array.Empty<StatsItemDto>();

  /// <summary>Gets or sets the user's most recent plays.</summary>
  public IReadOnlyList<StatsRecentDto> Recent { get; set; } = Array.Empty<StatsRecentDto>();

  /// <summary>Gets or sets the user's total number of requests (any status).</summary>
  public int RequestsTotal { get; set; }

  /// <summary>Gets or sets the user's requests awaiting an admin decision.</summary>
  public int RequestsPending { get; set; }

  /// <summary>Gets or sets the user's approved requests not yet available (in progress).</summary>
  public int RequestsApproved { get; set; }

  /// <summary>Gets or sets the user's requests now available in the library.</summary>
  public int RequestsAvailable { get; set; }

  /// <summary>Gets or sets the user's denied requests.</summary>
  public int RequestsDenied { get; set; }

  /// <summary>Gets or sets the user's used disk bytes (fulfilled requests at real size).</summary>
  public long QuotaUsedBytes { get; set; }

  /// <summary>Gets or sets the user's quota in bytes (0 when unlimited).</summary>
  public long QuotaTotalBytes { get; set; }

  /// <summary>Gets or sets a value indicating whether the user's quota is unlimited.</summary>
  public bool QuotaUnlimited { get; set; }

  /// <summary>Gets or sets the timestamp of the earliest play recorded on the server — i.e. how far back
  /// the statistics go (bounded by the history retention window). Null when nothing has been recorded yet.</summary>
  public DateTime? DataSinceUtc { get; set; }

  /// <summary>Gets or sets the user's rank by watch time among all viewers active in the window
  /// (1 = most watch time). 0 when the user has no plays in the window.</summary>
  public int RankByMinutes { get; set; }

  /// <summary>Gets or sets the number of viewers with activity in the window (the ranking denominator).</summary>
  public int RankedUsers { get; set; }

  /// <summary>Gets or sets the user's activity-over-time series (plays/minutes per day) for the window, oldest first.</summary>
  public IReadOnlyList<StatsDayDto> Daily { get; set; } = Array.Empty<StatsDayDto>();
}
