using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One answer of a <see cref="Poll"/>. Options carry their own id rather than being addressed by
/// position, so an edit that reorders them cannot silently move the votes already cast.
/// </summary>
public class PollOption
{
  /// <summary>Gets or sets the option id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the option label.</summary>
  public string Text { get; set; } = string.Empty;
}
