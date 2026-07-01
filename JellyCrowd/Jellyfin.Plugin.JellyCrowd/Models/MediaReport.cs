using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A user-submitted issue report about a title (e.g. wrong version, missing subtitles), triaged by admins.
/// </summary>
public class MediaReport
{
  /// <summary>Gets or sets the unique report id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>).</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the title (captured at report time).</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the reporting user's id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the reporting user's display name.</summary>
  public string UserName { get; set; } = string.Empty;

  /// <summary>Gets or sets the issue description.</summary>
  public string Message { get; set; } = string.Empty;

  /// <summary>Gets or sets the issue category: <c>bug</c> / <c>subtitles</c> / <c>audio</c> / <c>quality</c> / <c>other</c>.</summary>
  public string Type { get; set; } = "other";

  /// <summary>Gets or sets the UTC creation time.</summary>
  public DateTime CreatedAt { get; set; }

  /// <summary>Gets or sets a value indicating whether an admin marked the report resolved.</summary>
  public bool Resolved { get; set; }
}
