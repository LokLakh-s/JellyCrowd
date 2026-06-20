using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A user's personal notification-delivery preferences (in addition to the in-app bell). The server
/// (SMTP / ntfy) is admin-configured; the user supplies their own destination (email / ntfy topic).
/// </summary>
public class UserNotificationPrefs
{
  /// <summary>Gets or sets the user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether personal (external) delivery is enabled. Defaults to
  /// <c>true</c>; the in-app bell is unaffected by this flag.
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>Gets or sets the user's email address for notifications (empty = no email).</summary>
  public string? Email { get; set; }

  /// <summary>Gets or sets the user's ntfy topic (empty = no ntfy).</summary>
  public string? NtfyTopic { get; set; }
}
