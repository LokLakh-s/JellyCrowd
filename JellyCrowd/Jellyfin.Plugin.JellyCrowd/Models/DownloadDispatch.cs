using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyCrowd.Services;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The canonical, backend-agnostic payload describing an approved request to be fulfilled.
/// Sent verbatim (JSON) by the webhook backend and consumed internally by the Servarr backend.
/// Carries enough identity (TMDB id, type, title, year, season) for a receiver to map it to
/// Radarr/Sonarr or any custom automation.
/// </summary>
public sealed class DownloadDispatch
{
  /// <summary>
  /// Gets or sets the originating Jelly Crowd request id.
  /// </summary>
  public Guid RequestId { get; set; }

  /// <summary>
  /// Gets or sets the requesting Jellyfin user id.
  /// </summary>
  public Guid UserId { get; set; }

  /// <summary>
  /// Gets or sets the requesting user's display name.
  /// </summary>
  public string UserName { get; set; } = string.Empty;

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
  /// Gets or sets the release year, when known.
  /// </summary>
  public int? Year { get; set; }

  /// <summary>
  /// Gets or sets the release/first-air date (ISO string), when known.
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
  /// Gets or sets the TMDB relative poster path, when known.
  /// </summary>
  public string? PosterPath { get; set; }

  /// <summary>
  /// Gets or sets the UTC time the request was created.
  /// </summary>
  public DateTime RequestedAt { get; set; }

  /// <summary>
  /// Gets or sets the UTC date/time the user wants the request fulfilled, when set.
  /// </summary>
  public DateTime? DesiredAt { get; set; }

  /// <summary>
  /// Gets or sets the canonical TMDB web URL for the title.
  /// </summary>
  public string TmdbUrl { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets what the title's other active requests still cover, set when the request is cancelled or
  /// its media deleted: the backend withdraws only what none of them wants. Internal to Jelly Crowd, so it
  /// is left out of the payload sent to webhooks and scripts.
  /// </summary>
  [JsonIgnore]
  public IReadOnlyList<RequestScope> KeepScopes { get; set; } = Array.Empty<RequestScope>();

  /// <summary>
  /// Gets or sets a value indicating whether a purge leaves the files to Jellyfin, which deletes them right
  /// after: the backend then only drops the title. Both deleting the same folder at once made Radarr fail
  /// half-way through its own deletion. Internal to Jelly Crowd, like <see cref="KeepScopes"/>.
  /// </summary>
  [JsonIgnore]
  public bool KeepFiles { get; set; }
}
