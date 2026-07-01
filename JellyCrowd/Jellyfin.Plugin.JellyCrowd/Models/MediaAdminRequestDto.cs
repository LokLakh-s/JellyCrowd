using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One request for a title, shown to admins in the media detail popup: who wanted it, in what state, and
/// (for TV) which season/episode.
/// </summary>
public class MediaAdminRequestDto
{
  /// <summary>Gets or sets the requester's display name.</summary>
  public string UserName { get; set; } = string.Empty;

  /// <summary>Gets or sets the request status (<c>Pending</c> / <c>Approved</c> / <c>Available</c> / <c>Denied</c>).</summary>
  public string Status { get; set; } = string.Empty;

  /// <summary>Gets or sets the requested season (TV), if any.</summary>
  public int? Season { get; set; }

  /// <summary>Gets or sets the requested episode (TV), if any.</summary>
  public int? Episode { get; set; }

  /// <summary>Gets or sets when the request was made (UTC).</summary>
  public DateTime RequestedAt { get; set; }
}
