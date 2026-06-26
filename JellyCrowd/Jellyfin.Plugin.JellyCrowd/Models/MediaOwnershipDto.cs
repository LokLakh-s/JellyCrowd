using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One media item (movie, or a TV season/episode) and the users who currently own it (admin view).
/// Ownership = an active <see cref="RequestStatus.Available"/> request on that exact title scope.
/// </summary>
public class MediaOwnershipDto
{
  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>).</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the title's display name.</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the poster path (for the thumbnail), if known.</summary>
  public string? PosterPath { get; set; }

  /// <summary>Gets or sets the season number (TV scope), or <c>null</c> for a movie/whole title.</summary>
  public int? Season { get; set; }

  /// <summary>Gets or sets the episode number (TV scope), or <c>null</c>.</summary>
  public int? Episode { get; set; }

  /// <summary>Gets the users who currently own this title.</summary>
  public IList<OwnerDto> Owners { get; } = new List<OwnerDto>();
}
