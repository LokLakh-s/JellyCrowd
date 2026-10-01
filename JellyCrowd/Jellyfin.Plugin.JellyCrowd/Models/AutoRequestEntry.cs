using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A season Jelly Crowd has already dealt with automatically for a user, so it is never requested twice —
/// not even after the user cancelled it or an administrator denied it.
/// </summary>
public class AutoRequestEntry
{
  /// <summary>Gets or sets the user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>Gets or sets the show's TMDB id.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the season number.</summary>
  public int Season { get; set; }

  /// <summary>Gets or sets when the season was dealt with (UTC).</summary>
  public DateTime AtUtc { get; set; }
}
