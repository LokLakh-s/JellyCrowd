using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The payload a user posts to vote: the option ids they picked. Re-posting replaces their previous
/// vote while the poll is open.
/// </summary>
public class PollVoteDto
{
  /// <summary>Gets or sets the picked option ids.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the posted array.")]
  public Collection<Guid> OptionIds { get; set; } = new();
}
