using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// An in-app notification for a single user, shown in the header notification bell. Created when one
/// of the user's requests changes state (approved, denied, available).
/// </summary>
public class UserNotification
{
  /// <summary>Gets or sets the unique notification id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the recipient user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the lifecycle event key (e.g. <c>Approved</c>, <c>Denied</c>, <c>Available</c>).</summary>
  public string Event { get; set; } = string.Empty;

  /// <summary>Gets or sets the notification title (typically the media title).</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the human-readable message.</summary>
  public string Message { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB relative poster path, if any.</summary>
  public string? PosterPath { get; set; }

  /// <summary>Gets or sets the UTC creation time.</summary>
  public DateTime CreatedAt { get; set; }

  /// <summary>Gets or sets a value indicating whether the user has seen the notification.</summary>
  public bool Read { get; set; }
}
