using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Persists the end-credits sequence detected by fingerprinting a season's episode tails, so the
/// media-segment provider can serve it without re-analyzing.
/// <para>
/// This is a different signal from <see cref="IOutroStore"/>, which caches the brightness/silence
/// heuristic: an anime's ED (and any recurring credits sequence) is bright and sung, so it is invisible
/// to "dark and quiet" — but it is IDENTICAL across the episodes of a season, exactly like an OP, which
/// is what makes it findable by fingerprint.
/// </para>
/// </summary>
public interface IOutroSegmentStore
{
  /// <summary>
  /// Gets the cached end-credits sequence for an episode. A region with a negative start means "analyzed,
  /// but this season has no recurring credits" — distinct from <c>null</c>, which means "not yet analyzed".
  /// </summary>
  /// <param name="itemId">The episode's Jellyfin item id.</param>
  /// <returns>The region, the "none" sentinel, or <c>null</c> when it has never been analyzed.</returns>
  OutroRegion? Get(Guid itemId);

  /// <summary>
  /// Replaces the cached regions for a season's episodes.
  /// </summary>
  /// <param name="seasonOutros">Map of episode item id to its region (or the "none" sentinel).</param>
  void UpsertSeason(IReadOnlyDictionary<Guid, OutroRegion> seasonOutros);
}
