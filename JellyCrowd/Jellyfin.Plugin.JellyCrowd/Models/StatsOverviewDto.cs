using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The statistics overview shown on the admin dashboard: playback totals, top media/users, recent activity
/// and library counts.
/// </summary>
public class StatsOverviewDto
{
  /// <summary>Gets or sets the number of days the playback figures cover (0 = all time).</summary>
  public int WindowDays { get; set; }

  /// <summary>Gets or sets the total number of plays in the window.</summary>
  public int TotalPlays { get; set; }

  /// <summary>Gets or sets the total watched minutes in the window.</summary>
  public double TotalMinutes { get; set; }

  /// <summary>Gets or sets the number of distinct viewers in the window.</summary>
  public int UniqueUsers { get; set; }

  /// <summary>Gets or sets the number of movies in the library.</summary>
  public int LibraryMovies { get; set; }

  /// <summary>Gets or sets the number of shows in the library.</summary>
  public int LibraryShows { get; set; }

  /// <summary>Gets or sets the number of episodes in the library.</summary>
  public int LibraryEpisodes { get; set; }

  /// <summary>Gets or sets the most-played movies.</summary>
  public IReadOnlyList<StatsItemDto> TopMovies { get; set; } = Array.Empty<StatsItemDto>();

  /// <summary>Gets or sets the most-played shows (by episode plays).</summary>
  public IReadOnlyList<StatsItemDto> TopShows { get; set; } = Array.Empty<StatsItemDto>();

  /// <summary>Gets or sets the most active users (by watch time).</summary>
  public IReadOnlyList<StatsUserDto> TopUsers { get; set; } = Array.Empty<StatsUserDto>();

  /// <summary>Gets or sets the most recent plays.</summary>
  public IReadOnlyList<StatsRecentDto> Recent { get; set; } = Array.Empty<StatsRecentDto>();

  /// <summary>Gets or sets the activity-over-time series (plays/minutes per day), oldest first.</summary>
  public IReadOnlyList<StatsDayDto> Daily { get; set; } = Array.Empty<StatsDayDto>();

  /// <summary>Gets or sets the timestamp of the earliest play on record — how far back the statistics go
  /// (bounded by the history retention window). Null when nothing has been recorded yet.</summary>
  public DateTime? DataSinceUtc { get; set; }
}
