using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The current user's notification feed plus the unread count, for the header bell.
/// </summary>
public class UserNotificationsDto
{
  /// <summary>Gets the notifications, newest first.</summary>
  public IReadOnlyList<UserNotification> Items { get; init; } = new List<UserNotification>();

  /// <summary>Gets the number of unread notifications.</summary>
  public int Unread { get; init; }
}
