namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The outcome of pushing a group's library access onto its members' Jellyfin accounts.
/// </summary>
public class ApplyLibraryAccessResultDto
{
  /// <summary>
  /// Gets or sets the number of members whose policy was updated.
  /// </summary>
  public int Applied { get; set; }

  /// <summary>
  /// Gets or sets the number of members skipped (for example, the user no longer exists).
  /// </summary>
  public int Skipped { get; set; }

  /// <summary>
  /// Gets or sets the total number of members in the group.
  /// </summary>
  public int Total { get; set; }

  /// <summary>
  /// Gets or sets the number of libraries granted (existing libraries among the group's chosen set).
  /// </summary>
  public int Libraries { get; set; }
}
