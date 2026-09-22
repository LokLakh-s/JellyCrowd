using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A poll as delivered to a client, already resolved for the caller: what they may do with it
/// (<see cref="CanVote"/>), what they already answered (<see cref="MyOptionIds"/>) and whether the
/// tally is theirs to see (<see cref="ResultsVisible"/>).
/// </summary>
public class PollViewDto
{
  /// <summary>Gets or sets the poll id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the question.</summary>
  public string Question { get; set; } = string.Empty;

  /// <summary>Gets or sets the options, with the tally when <see cref="ResultsVisible"/> is set.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the array when the DTO is deserialized in tests.")]
  public Collection<PollOptionViewDto> Options { get; set; } = new();

  /// <summary>Gets or sets a value indicating whether the voter may pick several options.</summary>
  public bool MultiChoice { get; set; }

  /// <summary>Gets or sets the UTC closing time, or <c>null</c> when there is no deadline.</summary>
  public DateTime? ClosesAt { get; set; }

  /// <summary>Gets or sets a value indicating whether the poll still accepts votes.</summary>
  public bool Open { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether this caller may vote: they are in the poll's audience and
  /// it is still open. An administrator outside the audience sees the poll but does not vote in it.
  /// </summary>
  public bool CanVote { get; set; }

  /// <summary>Gets or sets a value indicating whether the caller has already voted.</summary>
  public bool Voted { get; set; }

  /// <summary>Gets or sets the option ids the caller picked.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the array when the DTO is deserialized in tests.")]
  public Collection<Guid> MyOptionIds { get; set; } = new();

  /// <summary>Gets or sets a value indicating whether the caller may see the tally.</summary>
  public bool ResultsVisible { get; set; }

  /// <summary>Gets or sets the number of users who voted, when the tally is visible.</summary>
  public int TotalVotes { get; set; }
}
