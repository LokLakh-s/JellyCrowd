namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A user's disk-quota usage snapshot.
/// </summary>
public class QuotaInfo
{
  /// <summary>
  /// Gets or sets the bytes currently used (sum of fulfilled requests' library sizes).
  /// </summary>
  public long UsedBytes { get; set; }

  /// <summary>
  /// Gets or sets the quota in bytes (0 when unlimited).
  /// </summary>
  public long QuotaBytes { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the user has no quota limit.
  /// </summary>
  public bool Unlimited { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the adaptive quota is in effect for this user.
  /// </summary>
  public bool AdaptiveEnabled { get; set; }

  /// <summary>
  /// Gets or sets the current adaptive tier (<c>floor</c>, <c>base</c> or <c>ceiling</c>), or <c>null</c>
  /// when the adaptive quota is off.
  /// </summary>
  public string? Tier { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the user is currently on probation (their quota is frozen).
  /// </summary>
  public bool InProbation { get; set; }

  /// <summary>
  /// Gets or sets the UTC time at which the current probation ends, or <c>null</c> when not on probation.
  /// </summary>
  public System.DateTime? ProbationEndsUtc { get; set; }
}
