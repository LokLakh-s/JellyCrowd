using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A title a user follows (watchlist) without necessarily requesting it.
/// </summary>
public class WatchlistEntry
{
  /// <summary>
  /// Gets or sets the unique entry identifier.
  /// </summary>
  public Guid Id { get; set; }

  /// <summary>
  /// Gets or sets the owning Jellyfin user.
  /// </summary>
  public Guid UserId { get; set; }

  /// <summary>
  /// Gets or sets the TMDB identifier of the followed title.
  /// </summary>
  public int TmdbId { get; set; }

  /// <summary>
  /// Gets or sets the media type, either <c>movie</c> or <c>tv</c>.
  /// </summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the display title (captured when added).
  /// </summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TMDB relative poster path.
  /// </summary>
  public string? PosterPath { get; set; }

  /// <summary>
  /// Gets or sets the release/first-air date (ISO string), captured when added.
  /// </summary>
  public string? ReleaseDate { get; set; }

  /// <summary>
  /// Gets or sets the UTC time the entry was added.
  /// </summary>
  public DateTime AddedAt { get; set; }
}
