namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Tuning for <see cref="SegmentDetection.DetectOutroSegments"/>. All durations are in seconds.
/// </summary>
/// <param name="DarkFraction">A second counts as "dark" when its luma is below this fraction of the clip's
/// reference brightness (a high percentile of the analyzed tail). Bit-depth independent.</param>
/// <param name="MinCreditRunSeconds">A dark-or-silent run must last at least this long to count as credits
/// (rejects a brief dark shot at the end of the story).</param>
/// <param name="MinBonusRunSeconds">A bright-with-audio gap inside the credits must last at least this long
/// to be treated as a real break (bonus); shorter bright blips are absorbed into the credits.</param>
/// <param name="MaxBonusGapSeconds">Two credit runs separated by a bright gap no longer than this are grouped
/// into one outro (a mid-credits bonus between them); a longer gap is the story body, so grouping stops.</param>
/// <param name="MaxTrailingBonusSeconds">At most this much bright content may follow the last credit run and
/// still be considered a post-credits bonus; more than this means the "credits" were really a dark scene, so
/// nothing is emitted.</param>
/// <param name="MinTrailingSilenceSeconds">Only a silence at least this long, running to the end of the item,
/// counts as credits (a silent/quiet end card). Scattered dialogue pauses are ignored — they are not credits.</param>
/// <param name="SilenceEndToleranceSeconds">How close to the end that trailing silence must reach.</param>
/// <param name="MinCreditsSeconds">The credits must begin at least this far from the end.</param>
/// <param name="MaxCreditsSeconds">…and at most this far (rejects marking something mid-content).</param>
public readonly record struct OutroDetectionOptions(
  double DarkFraction,
  double MinCreditRunSeconds,
  double MinBonusRunSeconds,
  double MaxBonusGapSeconds,
  double MaxTrailingBonusSeconds,
  double MinTrailingSilenceSeconds,
  double SilenceEndToleranceSeconds,
  double MinCreditsSeconds,
  double MaxCreditsSeconds);
