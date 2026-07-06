using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// The remembered result of outro analysis for a single item: the detected segments (an empty list means
/// the item was analyzed but has no credible outro), plus the inputs that would invalidate it. The segment
/// provider serves this straight back on the next Media Segment Scan instead of re-running the ffmpeg tail
/// analysis, and only re-analyzes when the file (size/modified/runtime) or the detection tuning changes.
/// </summary>
/// <param name="SizeBytes">The media file size when analyzed (from the library item); <c>-1</c> if unknown.</param>
/// <param name="ModifiedTicks">The item's last-modified timestamp (ticks) when analyzed.</param>
/// <param name="RuntimeTicks">The item runtime (ticks) when analyzed — it drives the analysis window.</param>
/// <param name="OptionsSignature">A signature of the outro-detection tuning used, so re-tuning re-analyzes.</param>
/// <param name="Regions">The detected outro segments (possibly empty = "analyzed, no outro").</param>
public sealed record OutroAnalysis(
  long SizeBytes,
  long ModifiedTicks,
  long RuntimeTicks,
  string OptionsSignature,
  IReadOnlyList<OutroRegion> Regions)
{
  /// <summary>
  /// Gets a value indicating whether this cached analysis is still valid for the given item state and
  /// detection tuning — i.e. the file is unchanged and the tuning matches, so its regions can be reused.
  /// </summary>
  /// <param name="sizeBytes">The current file size (from the item); <c>-1</c> if unknown.</param>
  /// <param name="modifiedTicks">The item's current last-modified timestamp, in ticks.</param>
  /// <param name="runtimeTicks">The item's current runtime, in ticks.</param>
  /// <param name="optionsSignature">The current detection-tuning signature.</param>
  /// <returns><c>true</c> if the cached result can be reused; otherwise <c>false</c>.</returns>
  public bool Matches(long sizeBytes, long modifiedTicks, long runtimeTicks, string optionsSignature)
    => SizeBytes == sizeBytes
       && ModifiedTicks == modifiedTicks
       && RuntimeTicks == runtimeTicks
       && string.Equals(OptionsSignature, optionsSignature, StringComparison.Ordinal);
}
