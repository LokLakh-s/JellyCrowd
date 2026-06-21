using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A single review as exposed to the client. The author name is only present for administrators
/// (reviews are anonymous to everyone else).
/// </summary>
public class ReviewView
{
  /// <summary>Gets or sets the review id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the rating (1–10, or 0 for a legacy text-only review).</summary>
  public int Rating { get; set; }

  /// <summary>Gets or sets the review text (may be empty).</summary>
  public string Text { get; set; } = string.Empty;

  /// <summary>Gets or sets the UTC creation time.</summary>
  public DateTime CreatedAt { get; set; }

  /// <summary>Gets or sets the author's name — only set for administrators (anonymous otherwise).</summary>
  public string? UserName { get; set; }

  /// <summary>Gets or sets a value indicating whether this review belongs to the current user.</summary>
  public bool Mine { get; set; }
}
