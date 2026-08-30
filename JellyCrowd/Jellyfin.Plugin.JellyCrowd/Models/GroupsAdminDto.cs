using System;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The admin groups overview: the groups (summaries) and which of them a targeted announcement is aimed
/// at, so an editor can preselect the current audience.
/// </summary>
public class GroupsAdminDto
{
  /// <summary>
  /// Gets the group summaries.
  /// </summary>
  public Collection<GroupSummaryDto> Groups { get; } = new();

  /// <summary>
  /// Gets the group ids the current announcement targets (empty means global).
  /// </summary>
  public Collection<Guid> AnnouncementGroupIds { get; } = new();
}
