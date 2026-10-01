using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A request a parent makes for one of their children.
/// </summary>
public class ChildRequestDto : CreateRequestDto
{
  /// <summary>
  /// Gets or sets the child the request is for.
  /// </summary>
  public Guid ChildId { get; set; }
}
