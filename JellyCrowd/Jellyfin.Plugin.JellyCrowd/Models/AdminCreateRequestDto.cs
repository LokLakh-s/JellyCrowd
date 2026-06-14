using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Admin payload to create a request on behalf of another user (bypasses quota and rate limits).
/// </summary>
public class AdminCreateRequestDto
{
  /// <summary>
  /// Gets or sets the target Jellyfin user id the request is created for.
  /// </summary>
  public Guid UserId { get; set; }

  /// <summary>
  /// Gets or sets the TMDB identifier of the requested title.
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
  /// Gets or sets the requested episode number within the season (null = whole season/movie).
  /// </summary>
  public int? Episode { get; set; }

  /// <summary>
  /// Gets or sets the initial status. Defaults to <see cref="RequestStatus.Approved"/> when omitted.
  /// </summary>
  public RequestStatus? Status { get; set; }
}
