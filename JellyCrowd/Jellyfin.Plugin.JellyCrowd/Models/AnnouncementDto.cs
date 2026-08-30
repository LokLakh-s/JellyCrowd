using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Admin payload to set (or clear) the header announcement banner.
/// </summary>
public class AnnouncementDto
{
  /// <summary>
  /// Gets or sets the announcement text. Empty clears the banner.
  /// </summary>
  public string Text { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the severity colour: <c>green</c>, <c>yellow</c> or <c>red</c>.
  /// </summary>
  public string Level { get; set; } = "green";

  /// <summary>
  /// Gets or sets the group ids the announcement targets. Empty means the announcement is global (shown
  /// to everyone).
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the posted array when deserializing the announcement payload.")]
  public Collection<Guid> GroupIds { get; set; } = new();
}
