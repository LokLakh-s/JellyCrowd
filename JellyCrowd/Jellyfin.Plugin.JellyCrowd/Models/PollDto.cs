using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Admin payload to create or edit a poll. Options are plain strings here: the server assigns their
/// ids, and an edit is only accepted while no vote has been cast.
/// </summary>
public class PollDto
{
  /// <summary>Gets or sets the question.</summary>
  public string Question { get; set; } = string.Empty;

  /// <summary>Gets or sets the option labels, in display order.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the posted array.")]
  public Collection<string> Options { get; set; } = new();

  /// <summary>Gets or sets a value indicating whether a voter may pick several options.</summary>
  public bool MultiChoice { get; set; }

  /// <summary>Gets or sets a value indicating whether voters see the tally once they have voted.</summary>
  public bool ShowResults { get; set; } = true;

  /// <summary>Gets or sets the UTC closing time; <c>null</c> means the poll stays open until closed by hand.</summary>
  public DateTime? ClosesAt { get; set; }

  /// <summary>Gets or sets the target group ids. Empty means the poll is global.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the posted array.")]
  public Collection<Guid> GroupIds { get; set; } = new();
}
