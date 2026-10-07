using System;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Media to give to, or take from, members: every media for every member.
/// </summary>
public class OwnershipChangeDto
{
  /// <summary>
  /// Gets or sets the members.
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Deserialized from the request body.")]
  public Collection<Guid> UserIds { get; set; } = new();

  /// <summary>
  /// Gets or sets the media.
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Deserialized from the request body.")]
  public Collection<OwnershipMediaRef> Media { get; set; } = new();
}
