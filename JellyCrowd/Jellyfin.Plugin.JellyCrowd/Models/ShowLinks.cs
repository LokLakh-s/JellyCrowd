using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// What ties a TMDB show to others: its names, start, creators and external ids. Used to find where Sonarr
/// files a show TMDB splits differently (a spin-off season listed as a show of its own).
/// </summary>
public class ShowLinks
{
  /// <summary>
  /// Gets or sets the TMDB show id.
  /// </summary>
  public int TmdbId { get; set; }

  /// <summary>
  /// Gets or sets the show's name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the show's original name.
  /// </summary>
  public string? OriginalName { get; set; }

  /// <summary>
  /// Gets or sets the TMDB ids of the show's creators.
  /// </summary>
  public IReadOnlyList<int> CreatorIds { get; set; } = Array.Empty<int>();

  /// <summary>
  /// Gets or sets the TVDB id TMDB links the show to, if any.
  /// </summary>
  public int? TvdbId { get; set; }
}
