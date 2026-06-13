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
}
