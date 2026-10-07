namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A media in an ownership change: a movie, a whole show, or a season of a show.
/// </summary>
public class OwnershipMediaRef
{
  /// <summary>
  /// Gets or sets the media type (<c>movie</c> or <c>tv</c>).
  /// </summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TMDB id.
  /// </summary>
  public int TmdbId { get; set; }

  /// <summary>
  /// Gets or sets the title (kept on the ownership it creates).
  /// </summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TMDB poster path, if known.
  /// </summary>
  public string? PosterPath { get; set; }

  /// <summary>
  /// Gets or sets the season (TMDB numbering), or <c>null</c> for a movie or a whole show.
  /// </summary>
  public int? Season { get; set; }

  /// <summary>
  /// Gets or sets the episode, or <c>null</c> for the whole season.
  /// </summary>
  public int? Episode { get; set; }

  /// <summary>
  /// Gets or sets the release date (kept on the ownership it creates), if known.
  /// </summary>
  public string? ReleaseDate { get; set; }
}
