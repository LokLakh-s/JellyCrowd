using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// An admin-authored poll, published alongside the announcements: a question, the options to pick from,
/// and the votes cast so far. Polls live in their own store rather than in the plugin configuration
/// because they carry per-user state (who voted what) the configuration is not meant to hold.
/// </summary>
public class Poll
{
  /// <summary>Gets or sets the poll id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the question shown to the user.</summary>
  public string Question { get; set; } = string.Empty;

  /// <summary>Gets or sets the options a voter picks from.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the stored array when deserializing the poll store.")]
  public Collection<PollOption> Options { get; set; } = new();

  /// <summary>Gets or sets a value indicating whether a voter may pick several options.</summary>
  public bool MultiChoice { get; set; }

  /// <summary>Gets or sets a value indicating whether voters see the tally once they have voted.</summary>
  public bool ShowResults { get; set; } = true;

  /// <summary>Gets or sets the UTC creation time.</summary>
  public DateTime CreatedAt { get; set; }

  /// <summary>Gets or sets the UTC time the poll closes on its own; <c>null</c> means no deadline.</summary>
  public DateTime? ClosesAt { get; set; }

  /// <summary>Gets or sets a value indicating whether an administrator closed the poll by hand.</summary>
  public bool Closed { get; set; }

  /// <summary>
  /// Gets or sets the UTC time the poll was closed by hand, so a closed poll can keep showing its
  /// result for a while before it stops being offered. <c>null</c> when it was never closed by hand.
  /// </summary>
  public DateTime? ClosedAt { get; set; }

  /// <summary>
  /// Gets or sets the group ids the poll targets. Empty means the poll is global (everyone may vote).
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the stored array when deserializing the poll store.")]
  public Collection<Guid> GroupIds { get; set; } = new();

  /// <summary>Gets or sets the votes cast, at most one per user.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the stored array when deserializing the poll store.")]
  public Collection<PollVote> Votes { get; set; } = new();
}
