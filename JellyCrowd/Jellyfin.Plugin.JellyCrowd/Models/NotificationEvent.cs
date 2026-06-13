namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A request lifecycle event that may trigger a notification.
/// </summary>
public enum NotificationEvent
{
  /// <summary>
  /// A user created a new request.
  /// </summary>
  Created,

  /// <summary>
  /// An administrator approved a request.
  /// </summary>
  Approved,

  /// <summary>
  /// An administrator denied a request.
  /// </summary>
  Denied,

  /// <summary>
  /// A requested title became available in the library.
  /// </summary>
  Available
}
