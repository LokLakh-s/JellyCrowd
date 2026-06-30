using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One completed viewing session captured from Jellyfin playback events: who watched what, when, and for
/// how long. Self-contained (user and item names resolved at capture time) so the statistics screens can
/// render without re-querying. Stored only while statistics capture is enabled.
/// </summary>
public class PlaybackRecord
{
  /// <summary>Gets or sets the unique record id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the viewer's Jellyfin user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the viewer's display name (captured at play time).</summary>
  public string UserName { get; set; } = string.Empty;

  /// <summary>Gets or sets the Jellyfin item id (32-char hex).</summary>
  public string ItemId { get; set; } = string.Empty;

  /// <summary>Gets or sets the item's display name (the episode title for episodes).</summary>
  public string ItemName { get; set; } = string.Empty;

  /// <summary>Gets or sets the item kind: <c>Movie</c>, <c>Episode</c>, <c>Audio</c> or another Jellyfin type.</summary>
  public string ItemType { get; set; } = string.Empty;

  /// <summary>Gets or sets the series name for episodes (empty otherwise).</summary>
  public string SeriesName { get; set; } = string.Empty;

  /// <summary>Gets or sets the series id for episodes (stable grouping key; empty otherwise).</summary>
  public string SeriesId { get; set; } = string.Empty;

  /// <summary>Gets or sets the season number for episodes, if known.</summary>
  public int? Season { get; set; }

  /// <summary>Gets or sets the episode number for episodes, if known.</summary>
  public int? Episode { get; set; }

  /// <summary>Gets or sets the client/device name the viewing happened on.</summary>
  public string Client { get; set; } = string.Empty;

  /// <summary>Gets or sets the UTC time the viewing started.</summary>
  public DateTime PlayedAtUtc { get; set; }

  /// <summary>Gets or sets the watched wall-clock minutes (estimated between progress pings, pauses excluded).</summary>
  public double Minutes { get; set; }
}
