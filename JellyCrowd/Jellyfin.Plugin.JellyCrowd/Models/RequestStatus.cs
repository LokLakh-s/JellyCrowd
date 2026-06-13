namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Lifecycle state of a media request.
/// </summary>
public enum RequestStatus
{
  /// <summary>
  /// Awaiting an administrator decision.
  /// </summary>
  Pending = 0,

  /// <summary>
  /// Approved by an administrator; awaiting fulfillment.
  /// </summary>
  Approved = 1,

  /// <summary>
  /// Rejected by an administrator.
  /// </summary>
  Denied = 2,

  /// <summary>
  /// Fulfilled: the media now exists in the Jellyfin library (resolved in milestone M3).
  /// </summary>
  Available = 3
}
