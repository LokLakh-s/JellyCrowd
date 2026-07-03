using System;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="SegmentDetection"/> — the pure ffmpeg-output parsing and outro heuristic.
/// </summary>
public class SegmentDetectionTests
{
  private static readonly DetectedRegion[] NoRegions = Array.Empty<DetectedRegion>();

  // Real jellyfin-ffmpeg output captured from a clip whose credits (black + silence) start at ~15s.
  private const string FfmpegOutput =
    "[Parsed_silencedetect_0 @ 0x1] silence_start: 15.010658\n" +
    "[Parsed_silencedetect_0 @ 0x1] silence_end: 20.03873 | silence_duration: 5.028073\n" +
    "[Parsed_blackdetect_0 @ 0x2] black_start:15.023047 black_end:19.923047 black_duration:4.9\n";

  [Fact]
  public void ParseRegions_ExtractsBlackAndSilence()
  {
    var (black, silence) = SegmentDetection.ParseRegions(FfmpegOutput, fallbackEnd: 20);

    var b = Assert.Single(black);
    Assert.Equal(15.023047, b.Start, 3);
    Assert.Equal(19.923047, b.End, 3);

    var s = Assert.Single(silence);
    Assert.Equal(15.010658, s.Start, 3);
    Assert.Equal(20.03873, s.End, 3);
  }

  [Fact]
  public void ParseRegions_ClosesUnterminatedSilence_AtFallback()
  {
    var (_, silence) = SegmentDetection.ParseRegions("[x] silence_start: 40.0\n", fallbackEnd: 55);

    Assert.Equal(55, Assert.Single(silence).End);
  }

  [Fact]
  public void ParseRegions_ReturnsEmpty_ForNoMatches()
  {
    var (black, silence) = SegmentDetection.ParseRegions("nothing here", 10);
    Assert.Empty(black);
    Assert.Empty(silence);
  }

  // ----- Anchor 1: a long silence running to the end (silent/quiet credits crawl) -----

  [Fact]
  public void DetectOutro_SilenceReachingEnd_AnchorsAtSilenceStart()
  {
    // 30-min episode (1800s); window is the last 360s (offset 1440). A 97s silence runs from relative
    // 263s (abs 1703 = 28m23s) to the file end — the ending crawl. Its start is the outro. This mirrors
    // real Devil May Cry S01E01, where an isolated 11s scene-fade earlier would have false-triggered the
    // old "earliest black" rule; that black is present here but the silence anchor takes precedence.
    var black = new[] { new DetectedRegion(114, 125), new DetectedRegion(346, 348) }; // 26m54s scene fade + a late fade
    var silence = new[] { new DetectedRegion(263, 360) }; // 28m23s → end, 97s

    var outro = SegmentDetection.DetectOutroStartSeconds(
      black, silence, offsetSeconds: 1440, runtimeSeconds: 1800,
      minLongBlackSeconds: 15, minSilenceRunSeconds: 25, silenceEndToleranceSeconds: 15,
      minCreditsSeconds: 20, maxCreditsSeconds: 900);

    Assert.Equal(1703, outro!.Value, 3);
  }

  [Fact]
  public void DetectOutro_InternalSilence_NotReachingEnd_IsIgnored()
  {
    // A 40s silence that ends 200s before the file end is a mid-content quiet passage, not credits.
    var silence = new[] { new DetectedRegion(100, 140) }; // abs 1540..1580, ends 220s before end
    Assert.Null(SegmentDetection.DetectOutroStartSeconds(
      NoRegions, silence, 1440, 1800, 15, 25, 15, 20, 900));
  }

  [Fact]
  public void DetectOutro_ShortSilence_IsIgnored()
  {
    var silence = new[] { new DetectedRegion(340, 360) }; // 20s < minSilenceRun, even though it reaches the end
    Assert.Null(SegmentDetection.DetectOutroStartSeconds(
      NoRegions, silence, 1440, 1800, 15, 25, 15, 20, 900));
  }

  // ----- Anchor 2: the earliest long black run (credits on black) -----

  [Fact]
  public void DetectOutro_LongBlack_AnchorsAndIgnoresShortSceneFades()
  {
    // 127-min film (7641s); window is the last 720s (offset 6921). Short scene fades near the tail must be
    // ignored; the first long (30s) black at rel 151s (abs 7072 = 117m52s) anchors the credits — matching
    // real "Michael", whose end credits have music (no silence anchor).
    var black = new[]
    {
      new DetectedRegion(10, 13),   // 3s scene fade — ignored
      new DetectedRegion(151, 181), // 30s credits-on-black — the anchor
      new DetectedRegion(199, 262), // later 63s black
    };

    var outro = SegmentDetection.DetectOutroStartSeconds(
      black, NoRegions, offsetSeconds: 6921, runtimeSeconds: 7641,
      minLongBlackSeconds: 15, minSilenceRunSeconds: 25, silenceEndToleranceSeconds: 15,
      minCreditsSeconds: 20, maxCreditsSeconds: 900);

    Assert.Equal(7072, outro!.Value, 3);
  }

  [Fact]
  public void DetectOutro_SilenceAnchor_TakesPrecedenceOverBlack()
  {
    var black = new[] { new DetectedRegion(50, 90) };      // abs 1490, a 40s long black
    var silence = new[] { new DetectedRegion(263, 360) };  // abs 1703 → end

    var outro = SegmentDetection.DetectOutroStartSeconds(
      black, silence, 1440, 1800, 15, 25, 15, 20, 900);

    Assert.Equal(1703, outro!.Value, 3); // silence wins even though the black is earlier
  }

  [Fact]
  public void DetectOutro_ReturnsNull_WhenNoAnchorQualifies()
  {
    // Only short blacks and no qualifying silence.
    var black = new[] { new DetectedRegion(100, 105), new DetectedRegion(300, 308) }; // 5s, 8s — below minLongBlack
    Assert.Null(SegmentDetection.DetectOutroStartSeconds(
      black, NoRegions, 1440, 1800, 15, 25, 15, 20, 900));
  }

  [Fact]
  public void DetectOutro_RejectsAnchors_OutsideCreditsBounds()
  {
    // Long black so late that < minCredits remains (remaining 5s).
    Assert.Null(SegmentDetection.DetectOutroStartSeconds(
      new[] { new DetectedRegion(715, 740) }, NoRegions, 6921, 7641, 15, 25, 15, 20, 900));
    // Long black so early that > maxCredits remains (remaining 1200s).
    Assert.Null(SegmentDetection.DetectOutroStartSeconds(
      new[] { new DetectedRegion(0, 30) }, NoRegions, 6000, 7641, 15, 25, 15, 20, 900));
  }
}
