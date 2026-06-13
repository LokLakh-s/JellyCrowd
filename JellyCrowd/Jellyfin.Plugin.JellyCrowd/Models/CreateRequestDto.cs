using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Payload sent by a user to create a media request.
/// </summary>
public class CreateRequestDto
{
  /// <summary>
  /// Gets or sets the TMDB identifier of the title to request.
  /// </summary>
  public int TmdbId { get; set; }

  /// <summary>
  /// Gets or sets the media type, either <c>movie</c> or <c>tv</c>.
  /// </summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the display title.
  /// </summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TMDB relative poster path.
  /// </summary>
  public string? PosterPath { get; set; }

  /// <summary>
  /// Gets or sets the release/first-air date (ISO string).
  /// </summary>
  public string? ReleaseDate { get; set; }

  /// <summary>
  /// Gets or sets the requested season number for shows (null = movie or whole show).
  /// </summary>
  public int? Season { get; set; }

  /// <summary>
  /// Gets or sets the date/time the user wants the request fulfilled. Null defaults to "now".
  /// Reserved for future Servarr/custom download-script scheduling.
  /// </summary>
  public DateTime? DesiredAt { get; set; }
}
