using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One entry of a user's own viewing history, as returned to the web pages. Structured (not a
/// pre-formatted line) so the client renders it in the user's language.
/// </summary>
public class PlaybackHistoryEntryDto
{
  /// <summary>Gets or sets the record id (used to delete this one entry).</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the played item's title (the episode name for a show).</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the item type (<c>Movie</c>, <c>Episode</c>, …).</summary>
  public string ItemType { get; set; } = string.Empty;

  /// <summary>Gets or sets the series name, when the item is an episode.</summary>
  public string SeriesName { get; set; } = string.Empty;

  /// <summary>Gets or sets the season number, when known.</summary>
  public int? Season { get; set; }

  /// <summary>Gets or sets the episode number, when known.</summary>
  public int? Episode { get; set; }

  /// <summary>Gets or sets the library the item belongs to.</summary>
  public string LibraryName { get; set; } = string.Empty;

  /// <summary>Gets or sets when it was watched (UTC).</summary>
  public DateTime PlayedAtUtc { get; set; }

  /// <summary>Gets or sets how many minutes were watched.</summary>
  public double Minutes { get; set; }
}
