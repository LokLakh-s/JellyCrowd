using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One user's vote on a <see cref="Poll"/>. Votes are nominative: the admin results view shows who
/// picked what and who has not voted yet, so the voter's name is stored with their answer.
/// </summary>
public class PollVote
{
  /// <summary>Gets or sets the voter's user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the voter's display name, resolved when the vote was cast.</summary>
  public string UserName { get; set; } = string.Empty;

  /// <summary>Gets or sets the option ids the voter picked (one, unless the poll is multi-choice).</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the stored array when deserializing the poll store.")]
  public Collection<Guid> OptionIds { get; set; } = new();

  /// <summary>Gets or sets the UTC time the vote was cast (or last changed).</summary>
  public DateTime VotedAt { get; set; }
}
