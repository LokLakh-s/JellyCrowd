using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A poll as shown on the admin page: the full tally with voter names, who has not voted yet, and the
/// settings needed to re-open the editor.
/// </summary>
public class PollAdminDto
{
  /// <summary>Gets or sets the poll id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the question.</summary>
  public string Question { get; set; } = string.Empty;

  /// <summary>Gets or sets the options with their tally and voters.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the array when the DTO is deserialized in tests.")]
  public Collection<PollOptionViewDto> Options { get; set; } = new();

  /// <summary>Gets or sets a value indicating whether a voter may pick several options.</summary>
  public bool MultiChoice { get; set; }

  /// <summary>Gets or sets a value indicating whether voters see the tally once they have voted.</summary>
  public bool ShowResults { get; set; }

  /// <summary>Gets or sets the UTC creation time.</summary>
  public DateTime CreatedAt { get; set; }

  /// <summary>Gets or sets the UTC closing time, or <c>null</c> when there is no deadline.</summary>
  public DateTime? ClosesAt { get; set; }

  /// <summary>Gets or sets a value indicating whether the poll still accepts votes.</summary>
  public bool Open { get; set; }

  /// <summary>Gets or sets a value indicating whether an administrator closed the poll by hand.</summary>
  public bool Closed { get; set; }

  /// <summary>Gets or sets the target group ids. Empty means the poll is global.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the array when the DTO is deserialized in tests.")]
  public Collection<Guid> GroupIds { get; set; } = new();

  /// <summary>Gets or sets the number of users who voted.</summary>
  public int TotalVotes { get; set; }

  /// <summary>Gets or sets the names of the users in the audience who have not voted yet.</summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the array when the DTO is deserialized in tests.")]
  public Collection<string> NotVoted { get; set; } = new();

  /// <summary>
  /// Gets or sets a value indicating whether the poll can still be edited. Editing is refused once a
  /// vote has been cast: changing the question or the options under the voters would rewrite what they
  /// answered.
  /// </summary>
  public bool Editable { get; set; }
}
