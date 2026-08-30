using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A lightweight view of a user group for admin UIs (for example, the announcement audience picker):
/// its id, name and member count, without the inherited settings or library ids.
/// </summary>
public class GroupSummaryDto
{
  /// <summary>
  /// Gets or sets the group id.
  /// </summary>
  public Guid Id { get; set; }

  /// <summary>
  /// Gets or sets the group's display name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the number of members in the group.
  /// </summary>
  public int MemberCount { get; set; }
}
