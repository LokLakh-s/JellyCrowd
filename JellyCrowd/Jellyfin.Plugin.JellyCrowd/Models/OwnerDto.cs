using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A single owner of a media item: their display name and when their ownership started.
/// </summary>
public class OwnerDto
{
  /// <summary>Gets or sets the owner's display name.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>Gets or sets when the title became available to (was claimed by) this user, if known.</summary>
  public DateTime? SinceUtc { get; set; }
}
