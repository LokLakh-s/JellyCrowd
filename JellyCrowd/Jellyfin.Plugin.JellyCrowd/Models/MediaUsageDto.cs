using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One of the current user's available titles, enriched with its on-disk size for the "My media" view.
/// </summary>
public class MediaUsageDto
{
  /// <summary>Gets or sets the originating request id (used for the deletion action).</summary>
  public Guid RequestId { get; set; }

  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>).</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the display title.</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB relative poster path.</summary>
  public string? PosterPath { get; set; }

  /// <summary>Gets or sets the requested season number, if any.</summary>
  public int? Season { get; set; }

  /// <summary>Gets or sets the Jellyfin library item id (for the deep-link), if known.</summary>
  public string? JellyfinItemId { get; set; }

  /// <summary>Gets or sets the on-disk size in bytes (summed over episodes for shows).</summary>
  public long SizeBytes { get; set; }

  /// <summary>Gets or sets the UTC time deletion was requested, if any.</summary>
  public DateTime? DeletionRequestedAt { get; set; }

  /// <summary>
  /// Gets or sets the UTC time the media is scheduled to be removed (deletion request + retention),
  /// when a deletion has been requested. Lets the UI show a countdown.
  /// </summary>
  public DateTime? DeletionAt { get; set; }

  /// <summary>
  /// Gets or sets the UTC time the ownership lapses (became available + the expiry window), when expiry
  /// is enabled. Lets the UI show an expiry countdown; re-claiming resets it.
  /// </summary>
  public DateTime? ExpiresAt { get; set; }
}
