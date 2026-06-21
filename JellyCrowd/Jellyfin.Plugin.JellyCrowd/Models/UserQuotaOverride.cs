using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A per-user policy override: disk quota plus request permissions. A user with no entry uses the
/// global defaults. Unset (null) fields fall back to the global default so an entry created for one
/// setting never silently changes the others.
/// </summary>
public class UserQuotaOverride
{
  /// <summary>
  /// Gets or sets the Jellyfin user identifier.
  /// </summary>
  public Guid UserId { get; set; }

  /// <summary>
  /// Gets or sets the quota in bytes for this user. <c>0</c> means unlimited; <c>null</c> means use
  /// the global default.
  /// </summary>
  public long? QuotaBytes { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether this user may create requests. <c>null</c> means allowed.
  /// </summary>
  public bool? CanRequest { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether this user's requests are auto-approved (trusted user),
  /// bypassing the admin queue. <c>null</c>/<c>false</c> means not trusted.
  /// </summary>
  public bool? AutoApprove { get; set; }

  /// <summary>
  /// Gets or sets a per-user override for the maximum number of requests per period. <c>null</c> means
  /// use the global limit.
  /// </summary>
  public int? MaxRequestsPerPeriod { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether this user may access the plugin while it is hidden from
  /// regular users ("config mode"). Lets the admin enable Jelly Crowd for specific users only.
  /// <c>null</c>/<c>false</c> means no access when hidden.
  /// </summary>
  public bool? PluginAccess { get; set; }
}
