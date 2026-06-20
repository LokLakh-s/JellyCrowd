using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A user comment on a catalog title (movie or show), shown under the synopsis.
/// </summary>
public class MediaComment
{
  /// <summary>Gets or sets the unique comment id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>).</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier of the commented title.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the author's user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the author's display name (captured at post time).</summary>
  public string UserName { get; set; } = string.Empty;

  /// <summary>Gets or sets the comment text.</summary>
  public string Text { get; set; } = string.Empty;

  /// <summary>Gets or sets the UTC creation time.</summary>
  public DateTime CreatedAt { get; set; }

  /// <summary>Gets or sets a value indicating whether an admin hid the comment.</summary>
  public bool Hidden { get; set; }
}
