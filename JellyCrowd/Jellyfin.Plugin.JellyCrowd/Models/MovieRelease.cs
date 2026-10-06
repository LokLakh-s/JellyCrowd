using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// When a movie comes out, as TMDB lists it: the earliest date of each kind of release, across all
/// countries, and the movie's production status.
/// </summary>
public class MovieRelease
{
  /// <summary>
  /// Gets or sets the earliest cinema release (TMDB types 2 and 3).
  /// </summary>
  public DateTime? Theatrical { get; set; }

  /// <summary>
  /// Gets or sets the earliest digital release (TMDB type 4; a TV premiere, type 6, counts as one, as it
  /// does for Radarr).
  /// </summary>
  public DateTime? Digital { get; set; }

  /// <summary>
  /// Gets or sets the earliest physical release (TMDB type 5).
  /// </summary>
  public DateTime? Physical { get; set; }

  /// <summary>
  /// Gets or sets TMDB's status for the movie (<c>Released</c>, <c>Post Production</c>, …), if known.
  /// </summary>
  public string? Status { get; set; }
}
