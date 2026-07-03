using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Persists detected episode intros so the media-segment provider can serve them without re-analyzing.
/// </summary>
public interface IIntroStore
{
  /// <summary>
  /// Gets the cached intro for an episode, or <c>null</c> if none is known.
  /// </summary>
  /// <param name="itemId">The episode's Jellyfin item id.</param>
  /// <returns>The intro segment, or <c>null</c>.</returns>
  IntroSegment? Get(Guid itemId);

  /// <summary>
  /// Replaces the cached intros for a season's episodes. A <c>null</c> value removes any cached intro for
  /// that episode (e.g. a premiere that shares no intro).
  /// </summary>
  /// <param name="seasonIntros">Map of episode item id to its intro (or <c>null</c> for none).</param>
  void UpsertSeason(IReadOnlyDictionary<Guid, IntroSegment?> seasonIntros);
}
