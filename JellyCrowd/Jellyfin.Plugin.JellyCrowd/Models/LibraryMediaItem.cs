namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A movie/show present in the Jellyfin library (with a TMDB id), used by the admin "library cleanup"
/// tool to surface orphan media (no Jelly Crowd owner) that can be deleted.
/// </summary>
public class LibraryMediaItem
{
  /// <summary>Gets or sets the Jellyfin library item id (32-char hex).</summary>
  public string JellyfinItemId { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>).</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the title.</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the on-disk size in bytes.</summary>
  public long SizeBytes { get; set; }

  /// <summary>Gets or sets the number of Jelly Crowd owners (active "available" requests). 0 = orphan.</summary>
  public int OwnerCount { get; set; }
}
