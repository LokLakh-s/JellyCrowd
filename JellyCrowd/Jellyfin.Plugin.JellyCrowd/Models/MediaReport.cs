using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A user-submitted issue report, triaged by admins. It usually concerns a title (wrong version, missing
/// subtitles…), but it may also stand on its own — playback trouble, an account problem, anything the
/// user needs to reach an administrator about. A general report carries no title:
/// <see cref="TmdbId"/> is 0 and <see cref="MediaType"/> is empty.
/// </summary>
public class MediaReport
{
  /// <summary>Gets or sets the unique report id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>), empty for a general report.</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier, 0 for a general report.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the title (captured at report time), empty for a general report.</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the reporting user's id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the reporting user's display name.</summary>
  public string UserName { get; set; } = string.Empty;

  /// <summary>Gets or sets the issue description.</summary>
  public string Message { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the issue category: <c>bug</c> / <c>subtitles</c> / <c>audio</c> / <c>quality</c> /
  /// <c>playback</c> / <c>account</c> / <c>other</c>.
  /// </summary>
  public string Type { get; set; } = "other";

  /// <summary>Gets or sets the UTC creation time.</summary>
  public DateTime CreatedAt { get; set; }

  /// <summary>Gets or sets a value indicating whether an admin marked the report resolved.</summary>
  public bool Resolved { get; set; }

  /// <summary>Gets or sets an optional admin note delivered to the reporter when the report is resolved.</summary>
  public string AdminResponse { get; set; } = string.Empty;
}
