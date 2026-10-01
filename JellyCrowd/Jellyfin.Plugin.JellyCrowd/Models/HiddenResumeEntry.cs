using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Something a user removed from their "Continue watching" and "Next up" rows: a movie, or a whole show
/// (removing one of its episodes removes the show). Nothing about playback is touched — the resume position
/// stays — and the entry goes away by itself as soon as the user plays it again.
/// </summary>
public class HiddenResumeEntry
{
  /// <summary>Gets or sets the user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the item the user removed (a movie, or the episode they removed it from).</summary>
  public Guid ItemId { get; set; }

  /// <summary>Gets or sets the show it belongs to, for an episode; <c>null</c> for a movie.</summary>
  public Guid? SeriesId { get; set; }

  /// <summary>Gets or sets the display name (the movie, or the show), for the management list.</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets when it was removed (UTC).</summary>
  public DateTime HiddenAtUtc { get; set; }
}
