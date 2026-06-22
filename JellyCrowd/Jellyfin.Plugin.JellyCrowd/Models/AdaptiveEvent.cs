namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The kind of adaptive-quota transition produced by the state machine.
/// </summary>
public enum AdaptiveEvent
{
  /// <summary>No change.</summary>
  None,

  /// <summary>The user became active and rose to the ceiling tier (from base/floor, not from probation).</summary>
  Promoted,

  /// <summary>A rewarded user went quiet long enough to be put on probation (quota frozen).</summary>
  ProbationStarted,

  /// <summary>The user became active again during probation; their reward (ceiling) is restored.</summary>
  ProbationPassed,

  /// <summary>Probation elapsed without renewed activity; the user dropped to the base quota.</summary>
  ProbationFailed,

  /// <summary>A never-rewarded, persistently inactive user decayed to the floor tier.</summary>
  DecayedToFloor,
}
