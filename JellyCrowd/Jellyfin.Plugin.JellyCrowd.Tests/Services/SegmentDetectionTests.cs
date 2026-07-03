using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="SegmentDetection"/> — the pure ffmpeg-output parsing and outro heuristic.
/// </summary>
public class SegmentDetectionTests
{
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

  [Fact]
  public void DetectOutro_ReturnsAbsoluteStart_ForACreditsSizedFade()
  {
    // 2h movie; analysis window is the last 720s (offset 6480). A fade at relative 420s → absolute 6900s,
    // leaving 300s of credits — a valid outro.
    var black = new[] { new DetectedRegion(420, 423) };

    var outro = SegmentDetection.DetectOutroStartSeconds(black, offsetSeconds: 6480, runtimeSeconds: 7200, minBlackSeconds: 0.4, minCreditsSeconds: 20, maxCreditsSeconds: 900);

    Assert.NotNull(outro);
    Assert.Equal(6900, outro!.Value, 3);
  }

  [Fact]
  public void DetectOutro_PicksEarliestQualifyingFade()
  {
    var black = new[] { new DetectedRegion(600, 602), new DetectedRegion(420, 423) };

    var outro = SegmentDetection.DetectOutroStartSeconds(black, 6480, 7200, 0.4, 20, 900);

    Assert.Equal(6900, outro!.Value, 3); // 6480+420, earliest of the two
  }

  [Fact]
  public void DetectOutro_Ignores_TooShortBlack_TooCloseToEnd_AndTooEarly()
  {
    // Too-short black.
    Assert.Null(SegmentDetection.DetectOutroStartSeconds(new[] { new DetectedRegion(420, 420.2) }, 6480, 7200, 0.4, 20, 900));
    // Black so late that < minCredits remains (remaining 5s).
    Assert.Null(SegmentDetection.DetectOutroStartSeconds(new[] { new DetectedRegion(715, 716) }, 6480, 7200, 0.4, 20, 900));
    // Black so early that > maxCredits remains (remaining 1200s).
    Assert.Null(SegmentDetection.DetectOutroStartSeconds(new[] { new DetectedRegion(0, 2) }, 6000, 7200, 0.4, 20, 900));
  }
}
