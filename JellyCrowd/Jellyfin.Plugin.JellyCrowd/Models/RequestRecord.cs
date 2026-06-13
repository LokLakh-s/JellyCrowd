using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A persisted media request made by a user.
/// </summary>
public class RequestRecord
{
  /// <summary>
  /// Gets or sets the unique request identifier.
  /// </summary>
  public Guid Id { get; set; }

  /// <summary>
  /// Gets or sets the Jellyfin user who made the request.
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
  /// Gets or sets the display title (captured at request time).
  /// </summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TMDB relative poster path.
  /// </summary>
  public string? PosterPath { get; set; }

  /// <summary>
  /// Gets or sets the release/first-air date (ISO string), captured at request time.
  /// </summary>
  public string? ReleaseDate { get; set; }

  /// <summary>
  /// Gets or sets the requested season number for shows (null = movie or whole show).
  /// </summary>
  public int? Season { get; set; }

  /// <summary>
  /// Gets or sets the current status.
  /// </summary>
  public RequestStatus Status { get; set; }

  /// <summary>
  /// Gets or sets the UTC time the request was created.
  /// </summary>
  public DateTime RequestedAt { get; set; }

  /// <summary>
  /// Gets or sets the UTC date/time the user wants the request to be fulfilled. Defaults to the
  /// creation time ("now"). Reserved for future Servarr/custom download-script scheduling.
  /// </summary>
  public DateTime? DesiredAt { get; set; }

  /// <summary>
  /// Gets or sets the UTC time an administrator approved or denied the request, if any.
  /// </summary>
  public DateTime? DecidedAt { get; set; }

  /// <summary>
  /// Gets or sets the administrator who approved or denied the request, if any.
  /// </summary>
  public Guid? DecidedBy { get; set; }

  /// <summary>
  /// Gets or sets the Jellyfin library item id (32-char hex) once the request is available; used for deletion.
  /// </summary>
  public string? JellyfinItemId { get; set; }

  /// <summary>
  /// Gets or sets the UTC time the user asked for this media to be deleted, if any.
  /// The scheduled deletion task removes it once the retention period has elapsed.
  /// </summary>
  public DateTime? DeletionRequestedAt { get; set; }
}
