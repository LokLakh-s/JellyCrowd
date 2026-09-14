using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A single user's quota usage snapshot, for the admin per-user quota view.
/// </summary>
public class UserQuotaInfoDto
{
  /// <summary>Gets or sets the user identifier.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the bytes currently used (real size of available media + estimates for in-flight requests).</summary>
  public long UsedBytes { get; set; }

  /// <summary>Gets or sets the bytes reserved by the user's in-flight requests (not yet on disk).</summary>
  public long ReservedBytes { get; set; }

  /// <summary>Gets or sets the effective quota in bytes (0 when unlimited).</summary>
  public long QuotaBytes { get; set; }

  /// <summary>Gets or sets a value indicating whether the user has no quota limit.</summary>
  public bool Unlimited { get; set; }

  /// <summary>Gets or sets the adaptive tier (<c>floor</c>/<c>base</c>/<c>ceiling</c>), or <c>null</c> when adaptive is off.</summary>
  public string? Tier { get; set; }
}
