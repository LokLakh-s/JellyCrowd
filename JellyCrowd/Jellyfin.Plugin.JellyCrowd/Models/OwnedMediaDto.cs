using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A media in the library (a movie, or a season of a show) with the members who own it.
/// </summary>
public class OwnedMediaDto
{
  /// <summary>
  /// Gets or sets the Jellyfin library item id (the movie, or the season).
  /// </summary>
  public string JellyfinItemId { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the media type (<c>movie</c> or <c>tv</c>).
  /// </summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TMDB id.
  /// </summary>
  public int TmdbId { get; set; }

  /// <summary>
  /// Gets or sets the title.
  /// </summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TMDB poster path, when a request recorded one.
  /// </summary>
  public string? PosterPath { get; set; }

  /// <summary>
  /// Gets or sets the season (TMDB numbering) for a show, or <c>null</c>.
  /// </summary>
  public int? Season { get; set; }

  /// <summary>
  /// Gets or sets the size on disk, in bytes.
  /// </summary>
  public long SizeBytes { get; set; }

  /// <summary>
  /// Gets the members who own it.
  /// </summary>
  public IList<MediaHolderDto> Owners { get; } = new List<MediaHolderDto>();
}
