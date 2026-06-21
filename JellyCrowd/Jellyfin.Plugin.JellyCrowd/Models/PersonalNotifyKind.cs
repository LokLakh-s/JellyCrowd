namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The category of a personal (external-channel) notification, used to honour each user's per-category
/// opt-in preferences. The in-app bell is unaffected by these categories; they only gate personal
/// delivery (email / ntfy).
/// </summary>
public enum PersonalNotifyKind
{
  /// <summary>No personal delivery applies (e.g. a lifecycle event nobody opts into per-channel).</summary>
  None,

  /// <summary>A title requested before its release became available (the deferred case).</summary>
  AvailableUnreleased,

  /// <summary>An ordinary request of an already-released title became available.</summary>
  AvailableReleased,

  /// <summary>A request decision: approved, denied or failed.</summary>
  Decision,

  /// <summary>A quota warning or an ownership-expiry warning.</summary>
  QuotaExpiry
}
