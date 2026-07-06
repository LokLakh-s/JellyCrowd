using System;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Remembers the outro-analysis result for each item so the media-segment provider serves the cached
/// segments on the next scan instead of re-running the (CPU/GPU-heavy) ffmpeg tail analysis. Entries are
/// invalidated by the caller when the file or the detection tuning changes (see <see cref="OutroAnalysis.Matches"/>).
/// </summary>
public interface IOutroStore
{
  /// <summary>
  /// Gets the remembered analysis for an item, or <c>null</c> if it was never analyzed.
  /// </summary>
  /// <param name="itemId">The item's Jellyfin id.</param>
  /// <returns>The cached analysis, or <c>null</c>.</returns>
  OutroAnalysis? Get(Guid itemId);

  /// <summary>
  /// Records (or replaces) the analysis for an item.
  /// </summary>
  /// <param name="itemId">The item's Jellyfin id.</param>
  /// <param name="analysis">The analysis to remember.</param>
  void Set(Guid itemId, OutroAnalysis analysis);

  /// <summary>
  /// Forgets any remembered analysis for an item (e.g. when Jellyfin cleans its extracted data), so it is
  /// analyzed afresh next time.
  /// </summary>
  /// <param name="itemId">The item's Jellyfin id.</param>
  void Remove(Guid itemId);
}
