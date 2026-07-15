using System.Linq;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Picks the end-credits region to expose for an item, with the same precedence the media-segment
/// provider serves it: the fingerprinted sequence (which sees an anime ED that the brightness/silence
/// heuristic cannot) wins, and the heuristic is the fallback. Pure, so the precedence is unit-tested.
/// </summary>
public static class OutroRegionResolver
{
  /// <summary>
  /// Resolves the region, or <c>null</c> when the item has no usable outro.
  /// </summary>
  /// <param name="fingerprinted">The fingerprint result, or <c>null</c>. A start below zero is the
  /// "analyzed, none" sentinel and is treated as no region.</param>
  /// <param name="heuristic">The brightness/silence analysis, or <c>null</c>.</param>
  /// <returns>The region ticks, or <c>null</c>.</returns>
  public static OutroRegion? Resolve(OutroRegion? fingerprinted, OutroAnalysis? heuristic)
  {
    if (fingerprinted is { StartTicks: >= 0 } fp && fp.EndTicks > fp.StartTicks)
    {
      return fp;
    }

    return heuristic?.Regions?.FirstOrDefault(r => r.EndTicks > r.StartTicks);
  }
}
