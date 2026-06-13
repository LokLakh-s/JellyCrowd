using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A single browsable catalog entry (movie or show) sourced from TMDB.
/// </summary>
public class CatalogItem
{
  /// <summary>
  /// Gets or sets the TMDB identifier.
  /// </summary>
  public int TmdbId { get; set; }

  /// <summary>
  /// Gets or sets the media type, either <c>movie</c> or <c>tv</c>.
  /// </summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the localized display title.
  /// </summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the synopsis.
  /// </summary>
  public string? Overview { get; set; }

  /// <summary>
  /// Gets or sets the TMDB relative poster path (e.g. <c>/abc.jpg</c>).
  /// </summary>
  public string? PosterPath { get; set; }

  /// <summary>
  /// Gets or sets the TMDB relative backdrop path.
  /// </summary>
  public string? BackdropPath { get; set; }

  /// <summary>
  /// Gets or sets the release date (movies) or first air date (shows), as an ISO string.
  /// </summary>
  public string? ReleaseDate { get; set; }

  /// <summary>
  /// Gets or sets the average TMDB rating (0-10).
  /// </summary>
  public double VoteAverage { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the item already exists in the Jellyfin library.
  /// Populated by library cross-referencing (milestone M3); defaults to <c>false</c>.
  /// </summary>
  public bool Available { get; set; }

  /// <summary>
  /// Gets or sets the Jellyfin library item id (32-char hex) when the title is available, else <c>null</c>.
  /// Lets the UI link straight to the item in Jellyfin.
  /// </summary>
  public string? JellyfinItemId { get; set; }

  /// <summary>
  /// Gets or sets the genre names (populated on detail lookups).
  /// </summary>
  public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();

  /// <summary>
  /// Gets or sets the runtime in minutes (movies, or a representative episode runtime for shows), if known.
  /// </summary>
  public int? Runtime { get; set; }

  /// <summary>
  /// Gets or sets the IMDb identifier (e.g. <c>tt1234567</c>), if known.
  /// </summary>
  public string? ImdbId { get; set; }
}
