using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// An admin-defined group of users. A user belongs to at most one group. The group carries default
/// Jelly Crowd settings its members inherit (a per-user override still wins over the group, which wins
/// over the global default) and a set of Jellyfin libraries the admin can push onto the members'
/// accounts. Groups can also be the audience of a targeted announcement.
/// </summary>
public class UserGroup
{
  /// <summary>Gets or sets the group id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the group's display name.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>Gets or sets the member user ids (a user should appear in at most one group).</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can replace it when deserializing the posted plugin configuration (a get-only collection is silently skipped on deserialize, which dropped the saved value).")]
  public Collection<Guid> Members { get; set; } = new();

  /// <summary>Gets or sets the group's default disk quota in bytes; <c>null</c> means "not set by the group".</summary>
  public long? QuotaBytes { get; set; }

  /// <summary>Gets or sets whether members may request; <c>null</c> means "not set by the group".</summary>
  public bool? CanRequest { get; set; }

  /// <summary>Gets or sets whether members' requests are auto-approved; <c>null</c> means "not set".</summary>
  public bool? AutoApprove { get; set; }

  /// <summary>Gets or sets the members' max requests per period; <c>null</c> means "not set".</summary>
  public int? MaxRequestsPerPeriod { get; set; }

  /// <summary>Gets or sets whether members may access the plugin; <c>null</c> means "not set".</summary>
  public bool? PluginAccess { get; set; }

  /// <summary>
  /// Gets or sets the Jellyfin library item ids to grant the members when the admin applies library
  /// access. Applying it sets each member's Jellyfin policy to exactly these libraries.
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can replace it when deserializing the posted plugin configuration (a get-only collection is silently skipped on deserialize, which dropped the saved value).")]
  public Collection<string> LibraryIds { get; set; } = new();
}
