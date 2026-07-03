using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure helpers that parse ffmpeg <c>blackdetect</c>/<c>silencedetect</c> output and derive an outro
/// (end-credits) start time. Network- and process-free so the detection logic is unit-tested against
/// captured ffmpeg text, independent of ffmpeg itself.
/// </summary>
public static class SegmentDetection
{
  // Examples (jellyfin-ffmpeg): "black_start:15.023 black_end:19.923 black_duration:4.9",
  // "silence_start: 15.010", "silence_end: 20.038 | silence_duration: 5.028".
  private static readonly Regex BlackRegex = new(
    @"black_start:(?<s>[0-9]+(?:\.[0-9]+)?)\s+black_end:(?<e>[0-9]+(?:\.[0-9]+)?)",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex SilenceStartRegex = new(
    @"silence_start:\s*(?<s>[0-9]+(?:\.[0-9]+)?)",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex SilenceEndRegex = new(
    @"silence_end:\s*(?<e>[0-9]+(?:\.[0-9]+)?)",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  /// <summary>
  /// Parses the black-frame and silence regions from captured ffmpeg output. Silence starts/ends are
  /// paired in order; a trailing unmatched silence_start is closed at <paramref name="fallbackEnd"/>.
  /// </summary>
  /// <param name="ffmpegOutput">The ffmpeg stderr text.</param>
  /// <param name="fallbackEnd">End time used to close an unterminated final silence (usually the window length).</param>
  /// <returns>The black and silence regions.</returns>
  public static (IReadOnlyList<DetectedRegion> Black, IReadOnlyList<DetectedRegion> Silence) ParseRegions(string ffmpegOutput, double fallbackEnd)
  {
    var black = new List<DetectedRegion>();
    var silence = new List<DetectedRegion>();
    if (string.IsNullOrEmpty(ffmpegOutput))
    {
      return (black, silence);
    }

    foreach (Match m in BlackRegex.Matches(ffmpegOutput))
    {
      black.Add(new DetectedRegion(ParseSeconds(m.Groups["s"].Value), ParseSeconds(m.Groups["e"].Value)));
    }

    // Silence is emitted as separate start/end lines; pair them in encounter order.
    var starts = new Queue<double>();
    foreach (Match m in SilenceStartRegex.Matches(ffmpegOutput))
    {
      starts.Enqueue(ParseSeconds(m.Groups["s"].Value));
    }

    var ends = new Queue<double>();
    foreach (Match m in SilenceEndRegex.Matches(ffmpegOutput))
    {
      ends.Enqueue(ParseSeconds(m.Groups["e"].Value));
    }

    while (starts.Count > 0)
    {
      var start = starts.Dequeue();
      var end = ends.Count > 0 ? ends.Dequeue() : fallbackEnd;
      silence.Add(new DetectedRegion(start, Math.Max(start, end)));
    }

    return (black, silence);
  }

  /// <summary>
  /// Derives the outro (end-credits) start time, in absolute seconds, from the detected black and silence
  /// regions. Two anchors, tried in order:
  /// <list type="number">
  ///   <item>a long, uninterrupted silence that runs to the end of the item — a silent/quiet credits
  ///   crawl (common on TV episode end cards); its start is the outro;</item>
  ///   <item>otherwise the earliest <em>long</em> black run — credits on a black background (common on
  ///   films). Short scene-transition fades are ignored so an isolated dramatic mid-tail fade is not
  ///   mistaken for the credits.</item>
  /// </list>
  /// The chosen point must leave a credits-sized remainder. Returns <c>null</c> when neither anchor fits —
  /// better no segment than one that skips into the content.
  /// </summary>
  /// <param name="black">Black regions, relative to the analyzed window.</param>
  /// <param name="silence">Silence regions, relative to the analyzed window.</param>
  /// <param name="offsetSeconds">Absolute start of the analyzed window (the ffmpeg seek point).</param>
  /// <param name="runtimeSeconds">Total item runtime in seconds.</param>
  /// <param name="minLongBlackSeconds">Minimum black duration to anchor credits-on-black (excludes scene fades).</param>
  /// <param name="minSilenceRunSeconds">Minimum silence duration to anchor a silent credits crawl.</param>
  /// <param name="silenceEndToleranceSeconds">How close to the runtime end a silence must reach to count as "to the end".</param>
  /// <param name="minCreditsSeconds">Credits must run at least this long after the anchor.</param>
  /// <param name="maxCreditsSeconds">…and at most this long (rejects mid-content anchors).</param>
  /// <returns>The absolute outro start in seconds, or <c>null</c>.</returns>
  public static double? DetectOutroStartSeconds(
    IReadOnlyList<DetectedRegion> black,
    IReadOnlyList<DetectedRegion> silence,
    double offsetSeconds,
    double runtimeSeconds,
    double minLongBlackSeconds,
    double minSilenceRunSeconds,
    double silenceEndToleranceSeconds,
    double minCreditsSeconds,
    double maxCreditsSeconds)
  {
    ArgumentNullException.ThrowIfNull(black);
    ArgumentNullException.ThrowIfNull(silence);

    // Anchor 1: a long silence reaching the end of the item marks a silent/quiet credits crawl.
    double? silenceAnchor = null;
    foreach (var region in silence)
    {
      if (region.Duration < minSilenceRunSeconds)
      {
        continue;
      }

      var absoluteEnd = offsetSeconds + region.End;
      if (absoluteEnd < runtimeSeconds - silenceEndToleranceSeconds)
      {
        continue; // an internal quiet passage, not the end credits
      }

      var absoluteStart = offsetSeconds + region.Start;
      if (silenceAnchor is null || absoluteStart < silenceAnchor.Value)
      {
        silenceAnchor = absoluteStart;
      }
    }

    if (silenceAnchor is double sa && IsCreditsSized(sa, runtimeSeconds, minCreditsSeconds, maxCreditsSeconds))
    {
      return sa;
    }

    // Anchor 2: the earliest long black run (credits on black).
    double? blackAnchor = null;
    foreach (var region in black)
    {
      if (region.Duration < minLongBlackSeconds)
      {
        continue;
      }

      var absoluteStart = offsetSeconds + region.Start;
      if (!IsCreditsSized(absoluteStart, runtimeSeconds, minCreditsSeconds, maxCreditsSeconds))
      {
        continue;
      }

      if (blackAnchor is null || absoluteStart < blackAnchor.Value)
      {
        blackAnchor = absoluteStart;
      }
    }

    return blackAnchor;
  }

  private static bool IsCreditsSized(double absoluteStart, double runtimeSeconds, double minCreditsSeconds, double maxCreditsSeconds)
  {
    var remaining = runtimeSeconds - absoluteStart;
    return remaining >= minCreditsSeconds && remaining <= maxCreditsSeconds;
  }

  private static double ParseSeconds(string value)
    => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
