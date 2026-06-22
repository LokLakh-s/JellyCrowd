using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The outcome of evaluating the adaptive-quota state machine for one user: the next tier/probation state
/// and the transition that occurred (so the caller can persist it and notify the user when relevant).
/// </summary>
public class AdaptiveTransition
{
  /// <summary>
  /// Gets or sets the resulting tier.
  /// </summary>
  public AdaptiveTier Tier { get; set; }

  /// <summary>
  /// Gets or sets the resulting probation start time (<c>null</c> when not on probation).
  /// </summary>
  public DateTime? ProbationStartUtc { get; set; }

  /// <summary>
  /// Gets or sets the resulting frozen quota in bytes (<c>null</c> when not on probation).
  /// </summary>
  public long? FrozenQuotaBytes { get; set; }

  /// <summary>
  /// Gets or sets the transition event that occurred.
  /// </summary>
  public AdaptiveEvent Event { get; set; }

  /// <summary>
  /// Gets a value indicating whether this transition changes anything (i.e. a non-<see cref="AdaptiveEvent.None"/> event).
  /// </summary>
  public bool Changed => Event != AdaptiveEvent.None;
}
