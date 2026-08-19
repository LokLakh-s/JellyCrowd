using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A user's own viewing history plus whether they have hidden it. When hidden, <see cref="Entries"/> is
/// empty (recording continues for the admin statistics; only the personal view is turned off).
/// </summary>
public class MyHistoryDto
{
  /// <summary>Gets or sets a value indicating whether the user has hidden their history.</summary>
  public bool Hidden { get; set; }

  /// <summary>Gets or sets the history entries, newest first (empty when hidden).</summary>
  public IReadOnlyList<PlaybackHistoryEntryDto> Entries { get; set; } = Array.Empty<PlaybackHistoryEntryDto>();
}
