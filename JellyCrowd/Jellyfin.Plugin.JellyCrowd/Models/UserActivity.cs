using System;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Per-user viewing-activity aggregate that drives the adaptive quota. Holds a bounded rolling window of
/// daily watch minutes (no per-title detail is kept) plus the user's current adaptive-quota tier and
/// probation state. Populated from Jellyfin playback events; only collected while the adaptive quota is enabled.
/// </summary>
public class UserActivity
{
  /// <summary>
  /// Gets or sets the user identifier.
  /// </summary>
  public Guid UserId { get; set; }

  /// <summary>
  /// Gets or sets the UTC timestamp of the user's most recent recorded playback activity.
  /// </summary>
  public DateTime LastSeenUtc { get; set; }

  /// <summary>
  /// Gets the rolling per-day watch totals (UTC days), pruned to a bounded window.
  /// </summary>
  public Collection<DailyWatch> Days { get; } = new();

  /// <summary>
  /// Gets or sets the user's current adaptive-quota tier.
  /// </summary>
  public AdaptiveTier Tier { get; set; } = AdaptiveTier.Base;

  /// <summary>
  /// Gets or sets the UTC time probation started, or <c>null</c> when the user is not on probation.
  /// During probation the effective quota is frozen at <see cref="FrozenQuotaBytes"/>.
  /// </summary>
  public DateTime? ProbationStartUtc { get; set; }

  /// <summary>
  /// Gets or sets the quota (bytes) frozen for the duration of probation, or <c>null</c> when not on probation.
  /// </summary>
  public long? FrozenQuotaBytes { get; set; }
}
