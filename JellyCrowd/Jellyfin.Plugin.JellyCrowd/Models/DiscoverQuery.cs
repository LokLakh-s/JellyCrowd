namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Filters for a TMDB discover query.
/// </summary>
public class DiscoverQuery
{
  /// <summary>
  /// Gets or sets a comma-separated list of TMDB genre ids to require.
  /// </summary>
  public string? Genres { get; set; }

  /// <summary>
  /// Gets or sets the earliest release/air year.
  /// </summary>
  public int? MinYear { get; set; }

  /// <summary>
  /// Gets or sets the latest release/air year.
  /// </summary>
  public int? MaxYear { get; set; }

  /// <summary>
  /// Gets or sets the minimum TMDB rating (0-10).
  /// </summary>
  public double? MinRating { get; set; }

  /// <summary>
  /// Gets or sets the maximum TMDB rating (0-10).
  /// </summary>
  public double? MaxRating { get; set; }

  /// <summary>
  /// Gets or sets the sort order: <c>rating</c>, <c>release</c>, or <c>popularity</c> (default).
  /// </summary>
  public string? SortBy { get; set; }

  /// <summary>
  /// Gets or sets the result page (1-based). Null/0 means page 1.
  /// </summary>
  public int? Page { get; set; }

  /// <summary>
  /// Gets or sets a comma-separated list of TMDB watch-provider ids to filter by (requires <see cref="WatchRegion"/>).
  /// </summary>
  public string? WatchProviders { get; set; }

  /// <summary>
  /// Gets or sets the ISO 3166-1 region for watch-provider filtering (e.g. <c>FR</c>, <c>US</c>).
  /// </summary>
  public string? WatchRegion { get; set; }
}
