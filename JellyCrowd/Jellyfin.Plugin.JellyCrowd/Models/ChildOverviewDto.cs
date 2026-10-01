using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One of a parent's children, as the parent sees them: their wishlist, their requests and their quota.
/// </summary>
public class ChildOverviewDto
{
  /// <summary>Gets or sets the child's user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the child's name.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>Gets or sets the child's age (0 = all ages).</summary>
  public int MaxAge { get; set; }

  /// <summary>Gets or sets the titles the child wishes for (their watchlist), newest first.</summary>
  public IReadOnlyList<WatchlistEntry> Wishlist { get; set; } = Array.Empty<WatchlistEntry>();

  /// <summary>Gets or sets the child's requests, newest first.</summary>
  public IReadOnlyList<RequestRecord> Requests { get; set; } = Array.Empty<RequestRecord>();

  /// <summary>Gets or sets the child's quota usage.</summary>
  public QuotaInfo? Quota { get; set; }
}
