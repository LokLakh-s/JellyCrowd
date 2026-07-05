using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure helpers that parse ffmpeg <c>silencedetect</c>/<c>signalstats</c> output and derive the outro
/// (end-credits) segments of an item. Network- and process-free so the logic is unit-tested against
/// captured ffmpeg text, independent of ffmpeg itself.
/// </summary>
/// <remarks>
/// End credits are recognised as a run of seconds that are <em>dark</em> (low luma) or <em>silent</em>.
/// A post-credits bonus scene is <em>bright with audio</em>, so it breaks that run — which lets the outro
/// stop at the bonus instead of skipping through it, and lets a mid-credits bonus split the outro into two
/// segments (Jellyfin draws a Skip button per segment).
/// </remarks>
public static class SegmentDetection
{
  // Examples (jellyfin-ffmpeg): "silence_start: 15.010", "silence_end: 20.038 | silence_duration: 5.028",
  // "black_start:15.02 black_end:19.92 black_duration:4.9", "pts_time:41" and "lavfi.signalstats.YAVG=16.2".
  private static readonly Regex BlackRegex = new(
    @"black_start:(?<s>[0-9]+(?:\.[0-9]+)?)\s+black_end:(?<e>[0-9]+(?:\.[0-9]+)?)",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex SilenceStartRegex = new(
    @"silence_start:\s*(?<s>[0-9]+(?:\.[0-9]+)?)",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex SilenceEndRegex = new(
    @"silence_end:\s*(?<e>[0-9]+(?:\.[0-9]+)?)",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex PtsTimeRegex = new(
    @"pts_time:(?<t>[0-9]+(?:\.[0-9]+)?)",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex YavgRegex = new(
    @"lavfi\.signalstats\.YAVG=(?<v>[0-9]+(?:\.[0-9]+)?)",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  /// <summary>
  /// Builds the ffmpeg arguments for the outro (end-credits) analysis: one decode of the file tail from
  /// <paramref name="offsetSeconds"/>, sampled at 1 fps for per-second average luma (signalstats) plus audio
  /// silence detection. The video decode can be offloaded to the GPU via <paramref name="hwAccel"/>.
  /// </summary>
  /// <param name="hwAccel">Hardware-accel mode (auto/none/vaapi/qsv/cuda/videotoolbox).</param>
  /// <param name="offsetSeconds">The seek point (absolute seconds) where the analyzed tail starts.</param>
  /// <param name="path">The media file path.</param>
  /// <returns>The ffmpeg argument string.</returns>
  public static string BuildOutroAnalyzeArgs(string? hwAccel, double offsetSeconds, string path)
    => string.Format(
      CultureInfo.InvariantCulture,
      "-hide_banner -nostats {0}-ss {1:0.###} -i \"{2}\" -vf fps=1,signalstats,metadata=print -af silencedetect=noise=-45dB:d=0.8 -f null -",
      HwAccelArg(hwAccel),
      offsetSeconds,
      path);

  /// <summary>
  /// Maps a hardware-acceleration mode to the ffmpeg <c>-hwaccel</c> input option (with a trailing space),
  /// or an empty string for CPU decoding. Unknown, empty or <c>none</c> values fall back to CPU.
  /// </summary>
  /// <param name="mode">The configured mode.</param>
  /// <returns>The ffmpeg input-option prefix (e.g. <c>"-hwaccel auto "</c>) or an empty string.</returns>
  public static string HwAccelArg(string? mode) => mode?.Trim().ToUpperInvariant() switch
  {
    "AUTO" => "-hwaccel auto ",
    "VAAPI" => "-hwaccel vaapi ",
    "QSV" => "-hwaccel qsv ",
    "CUDA" => "-hwaccel cuda ",
    "VIDEOTOOLBOX" => "-hwaccel videotoolbox ",
    _ => string.Empty,
  };

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
  /// Parses per-frame average-luma samples from ffmpeg <c>signalstats,metadata=print</c> output. The frame
  /// time (<c>pts_time</c>) and its <c>YAVG</c> value are printed on separate lines; each YAVG is paired
  /// with the most recent pts_time.
  /// </summary>
  /// <param name="ffmpegOutput">The ffmpeg stderr text.</param>
  /// <returns>The luma samples in encounter (time) order.</returns>
  public static IReadOnlyList<LumaSample> ParseLumaSamples(string ffmpegOutput)
  {
    var samples = new List<LumaSample>();
    if (string.IsNullOrEmpty(ffmpegOutput))
    {
      return samples;
    }

    double? time = null;
    foreach (var line in ffmpegOutput.Split('\n'))
    {
      var pt = PtsTimeRegex.Match(line);
      if (pt.Success)
      {
        time = ParseSeconds(pt.Groups["t"].Value);
      }

      var yv = YavgRegex.Match(line);
      if (yv.Success && time is double t)
      {
        samples.Add(new LumaSample(t, ParseSeconds(yv.Groups["v"].Value)));
      }
    }

    return samples;
  }

  /// <summary>
  /// Derives the outro (end-credits) segments, in absolute seconds, from the luma and silence of the file
  /// tail. Seconds that are dark or silent are treated as credits; a bright-with-audio stretch is content
  /// (story body before the credits, or a bonus scene during/after them). The trailing credits — grouped
  /// across any mid-credits bonus — become one segment each, so a post-credits bonus is never skipped.
  /// Returns an empty list when no credible credits are found.
  /// </summary>
  /// <param name="luma">Luma samples, relative to the analyzed window.</param>
  /// <param name="silence">Silence regions, relative to the analyzed window.</param>
  /// <param name="offsetSeconds">Absolute start of the analyzed window (the ffmpeg seek point).</param>
  /// <param name="runtimeSeconds">Total item runtime in seconds.</param>
  /// <param name="options">Detection tuning.</param>
  /// <returns>The outro segments in ascending order (absolute seconds); possibly empty.</returns>
  public static IReadOnlyList<DetectedRegion> DetectOutroSegments(
    IReadOnlyList<LumaSample> luma,
    IReadOnlyList<DetectedRegion> silence,
    double offsetSeconds,
    double runtimeSeconds,
    OutroDetectionOptions options)
  {
    ArgumentNullException.ThrowIfNull(luma);
    ArgumentNullException.ThrowIfNull(silence);

    var windowSeconds = (int)Math.Ceiling(runtimeSeconds - offsetSeconds);
    if (windowSeconds <= 0)
    {
      return Array.Empty<DetectedRegion>();
    }

    var creditLike = ClassifyCreditSeconds(luma, silence, windowSeconds, options);
    var runs = ToRuns(creditLike);
    Bridge(runs, options.MinBonusRunSeconds);

    var creditRuns = runs.Where(r => r.IsCredit && (r.End - r.Start) >= options.MinCreditRunSeconds).ToList();
    if (creditRuns.Count == 0)
    {
      return Array.Empty<DetectedRegion>();
    }

    // Group the trailing credit runs: start from the last and walk back while the bright gap to the
    // previous run is short enough to be a mid-credits bonus (not the story body).
    var cluster = new List<Run> { creditRuns[^1] };
    for (var i = creditRuns.Count - 2; i >= 0; i--)
    {
      var gap = cluster[0].Start - creditRuns[i].End;
      if (gap > options.MaxBonusGapSeconds)
      {
        break;
      }

      cluster.Insert(0, creditRuns[i]);
    }

    // Reject when too much bright content follows the last credit run (then it was a dark scene, not
    // credits), or when the credits sit outside the plausible end-of-item window.
    var trailingContent = windowSeconds - cluster[^1].End;
    if (trailingContent > options.MaxTrailingBonusSeconds)
    {
      return Array.Empty<DetectedRegion>();
    }

    var firstStart = offsetSeconds + cluster[0].Start;
    var remaining = runtimeSeconds - firstStart;
    if (remaining < options.MinCreditsSeconds || remaining > options.MaxCreditsSeconds)
    {
      return Array.Empty<DetectedRegion>();
    }

    var segments = new List<DetectedRegion>();
    foreach (var run in cluster)
    {
      var start = offsetSeconds + run.Start;
      var end = Math.Min(offsetSeconds + run.End, runtimeSeconds);
      if (end > start)
      {
        segments.Add(new DetectedRegion(start, end));
      }
    }

    return segments;
  }

  private static bool[] ClassifyCreditSeconds(
    IReadOnlyList<LumaSample> luma,
    IReadOnlyList<DetectedRegion> silence,
    int windowSeconds,
    OutroDetectionOptions options)
  {
    // Per-second average luma (buckets), and a brightness reference (high percentile) so the dark
    // threshold is relative to this clip's own brightness — independent of 8- vs 10-bit encoding.
    var bucketSum = new double[windowSeconds];
    var bucketCount = new int[windowSeconds];
    foreach (var s in luma)
    {
      var idx = (int)Math.Floor(s.TimeSeconds);
      if (idx >= 0 && idx < windowSeconds)
      {
        bucketSum[idx] += s.Value;
        bucketCount[idx]++;
      }
    }

    var darkThreshold = Percentile(luma.Select(l => l.Value).ToList(), 0.90) * options.DarkFraction;

    // Silence only counts as credits when it is a single long run reaching the end (a silent/quiet end
    // card) — not the scattered dialogue pauses that pepper the body of an episode.
    var trailingSilence = FindTrailingSilence(silence, windowSeconds, options.MinTrailingSilenceSeconds, options.SilenceEndToleranceSeconds);

    var creditLike = new bool[windowSeconds];
    for (var s = 0; s < windowSeconds; s++)
    {
      var dark = bucketCount[s] > 0 && (bucketSum[s] / bucketCount[s]) < darkThreshold;
      var silent = trailingSilence is DetectedRegion ts && ts.Start < s + 1 && ts.End > s;
      creditLike[s] = dark || silent;
    }

    return creditLike;
  }

  private static DetectedRegion? FindTrailingSilence(
    IReadOnlyList<DetectedRegion> silence,
    int windowSeconds,
    double minTrailingSilenceSeconds,
    double silenceEndToleranceSeconds)
  {
    DetectedRegion? best = null;
    foreach (var region in silence)
    {
      if (region.Duration >= minTrailingSilenceSeconds
          && region.End >= windowSeconds - silenceEndToleranceSeconds
          && (best is null || region.Start < best.Value.Start))
      {
        best = region;
      }
    }

    return best;
  }

  private static List<Run> ToRuns(bool[] creditLike)
  {
    var runs = new List<Run>();
    var i = 0;
    while (i < creditLike.Length)
    {
      var value = creditLike[i];
      var start = i;
      while (i < creditLike.Length && creditLike[i] == value)
      {
        i++;
      }

      runs.Add(new Run(value, start, i));
    }

    return runs;
  }

  // Absorb short content runs (bright blips) that sit between two credit runs into the credits, so a
  // studio card or a flash during the crawl does not fragment a single credits block.
  private static void Bridge(List<Run> runs, double minBonusRunSeconds)
  {
    for (var i = 1; i < runs.Count - 1; i++)
    {
      if (!runs[i].IsCredit && runs[i - 1].IsCredit && runs[i + 1].IsCredit
          && (runs[i].End - runs[i].Start) < minBonusRunSeconds)
      {
        var merged = new Run(true, runs[i - 1].Start, runs[i + 1].End);
        runs.RemoveRange(i - 1, 3);
        runs.Insert(i - 1, merged);
        i = Math.Max(0, i - 2);
      }
    }
  }

  private static double Percentile(List<double> values, double p)
  {
    if (values.Count == 0)
    {
      return 0;
    }

    values.Sort();
    var rank = (int)Math.Clamp(Math.Ceiling(p * values.Count) - 1, 0, values.Count - 1);
    return values[rank];
  }

  private static double ParseSeconds(string value)
    => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

  private readonly record struct Run(bool IsCredit, int Start, int End);
}
