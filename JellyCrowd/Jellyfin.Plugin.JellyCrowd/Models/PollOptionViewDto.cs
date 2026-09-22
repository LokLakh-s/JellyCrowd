using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One option as shown to a client. The tally (<see cref="Votes"/>, <see cref="Percent"/>) is filled in
/// only when the caller is allowed to see the results; otherwise both stay at zero.
/// </summary>
public class PollOptionViewDto
{
  /// <summary>Gets or sets the option id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the option label.</summary>
  public string Text { get; set; } = string.Empty;

  /// <summary>Gets or sets how many voters picked this option.</summary>
  public int Votes { get; set; }

  /// <summary>
  /// Gets or sets the share of voters who picked this option, in percent. On a multi-choice poll the
  /// base is the number of voters, not of picks, so the percentages can add up to more than 100.
  /// </summary>
  public int Percent { get; set; }

  /// <summary>
  /// Gets or sets the names of the users who picked this option. Administrators only — it stays empty
  /// for everyone else.
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can bind the array when the DTO is deserialized in tests.")]
  public Collection<string> Voters { get; set; } = new();
}
