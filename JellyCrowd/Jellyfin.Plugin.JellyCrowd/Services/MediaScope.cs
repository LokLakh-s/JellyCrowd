namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Season/episode scope arithmetic for TV ownership. A request (or a library entry) is scoped to a whole
/// series (<c>season == null</c>), a whole season (<c>episode == null</c>), or a single episode. Two scopes
/// for the SAME show "overlap" when they share at least one episode — this is what makes ownership,
/// orphan detection and the shared-media deletion guard work per season instead of per whole series.
/// </summary>
public static class MediaScope
{
  /// <summary>
  /// Gets a value indicating whether two season/episode scopes for the same show overlap (share files).
  /// <c>null</c> season means the whole series; <c>null</c> episode means the whole season. Symmetric.
  /// </summary>
  /// <param name="seasonA">First scope's season (<c>null</c> = whole series).</param>
  /// <param name="episodeA">First scope's episode (<c>null</c> = whole season).</param>
  /// <param name="seasonB">Second scope's season (<c>null</c> = whole series).</param>
  /// <param name="episodeB">Second scope's episode (<c>null</c> = whole season).</param>
  /// <returns><c>true</c> if the scopes share any content; otherwise <c>false</c>.</returns>
  public static bool Overlaps(int? seasonA, int? episodeA, int? seasonB, int? episodeB)
  {
    // One side covers the whole series → it necessarily includes the other.
    if (seasonA is null || seasonB is null)
    {
      return true;
    }

    // Different seasons never share content.
    if (seasonA.Value != seasonB.Value)
    {
      return false;
    }

    // Same season: overlap unless both pin a different single episode.
    return episodeA is null || episodeB is null || episodeA.Value == episodeB.Value;
  }
}
