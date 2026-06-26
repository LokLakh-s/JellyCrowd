using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A review as exposed to an administrator on the Moderation page: the full record (title reference,
/// author, content, and the hidden flag) so it can be moderated (hidden/shown/deleted).
/// </summary>
public class ModeratedReviewDto
{
  /// <summary>Gets or sets the review id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>).</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier of the reviewed title.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the title's display name (captured at post time; may be empty for legacy reviews).</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the author's display name.</summary>
  public string UserName { get; set; } = string.Empty;

  /// <summary>Gets or sets the rating (1–10, or 0 for a legacy text-only review).</summary>
  public int Rating { get; set; }

  /// <summary>Gets or sets the review text (may be empty).</summary>
  public string Text { get; set; } = string.Empty;

  /// <summary>Gets or sets the UTC creation time.</summary>
  public DateTime CreatedAt { get; set; }

  /// <summary>Gets or sets a value indicating whether the review is currently hidden from the public list.</summary>
  public bool Hidden { get; set; }
}
