using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A per-user disk quota that overrides the global default.
/// </summary>
public class UserQuotaOverride
{
  /// <summary>
  /// Gets or sets the Jellyfin user identifier.
  /// </summary>
  public Guid UserId { get; set; }

  /// <summary>
  /// Gets or sets the quota in bytes for this user. 0 means unlimited.
  /// </summary>
  public long QuotaBytes { get; set; }
}
