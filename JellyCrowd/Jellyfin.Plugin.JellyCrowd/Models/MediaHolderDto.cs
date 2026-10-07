using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A member owning a media, and how: the media itself, the whole show it belongs to, or some of its episodes.
/// </summary>
public class MediaHolderDto
{
  /// <summary>
  /// Gets or sets the member's id.
  /// </summary>
  public Guid UserId { get; set; }

  /// <summary>
  /// Gets or sets the member's name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets since when they own it (UTC).
  /// </summary>
  public DateTime? SinceUtc { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether they own it through the whole show.
  /// </summary>
  public bool WholeShow { get; set; }

  /// <summary>
  /// Gets the episodes they own, when they own only some of the season.
  /// </summary>
  public IList<int> Episodes { get; } = new List<int>();

  /// <summary>
  /// Gets or sets a value indicating whether they asked for its deletion (still theirs until it is done).
  /// </summary>
  public bool Leaving { get; set; }
}
