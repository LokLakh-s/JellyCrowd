using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A child account, set up by an administrator: the child browses an age-filtered catalog and keeps a
/// wishlist, but requests nothing — their parents request for them, on the child's own quota, and only
/// titles suited to the child's age. A child can have several parents.
/// </summary>
public class ChildAccount
{
  /// <summary>Gets or sets the child's user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the user ids of the parents who request for the child.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can replace it when deserializing the posted plugin configuration.")]
  public Collection<Guid> ParentIds { get; set; } = new();

  /// <summary>
  /// Gets or sets the child's age: titles rated above it are neither shown to the child nor requestable for
  /// them (0 = all ages).
  /// </summary>
  public int MaxAge { get; set; }
}
