using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="SegmentDetection"/> — the pure ffmpeg-output parsing and the luma/silence outro
/// segmenter (which keeps a post-credits bonus scene un-skipped).
/// </summary>
public class SegmentDetectionTests
{
  private static readonly DetectedRegion[] NoSilence = Array.Empty<DetectedRegion>();

  // Defaults mirroring PluginConfiguration.
  private static OutroDetectionOptions Options() => new(
    DarkFraction: 0.25,
    MinCreditRunSeconds: 15,
    MinBonusRunSeconds: 10,
    MaxBonusGapSeconds: 90,
    MaxTrailingBonusSeconds: 150,
    MinTrailingSilenceSeconds: 20,
    SilenceEndToleranceSeconds: 15,
    MinCreditsSeconds: 20,
    MaxCreditsSeconds: 900);

  // Builds 1 Hz luma samples over [0, window) from a per-second brightness function.
  private static List<LumaSample> Luma(int window, Func<int, double> brightness)
  {
    var list = new List<LumaSample>(window);
    for (var s = 0; s < window; s++)
    {
      list.Add(new LumaSample(s, brightness(s)));
    }

    return list;
  }

  // ----- ffmpeg argument building (GPU hardware acceleration) -----

  [Theory]
  [InlineData("auto", "-hwaccel auto ")]
  [InlineData("AUTO", "-hwaccel auto ")]
  [InlineData(" vaapi ", "-hwaccel vaapi ")]
  [InlineData("qsv", "-hwaccel qsv ")]
  [InlineData("cuda", "-hwaccel cuda ")]
  [InlineData("videotoolbox", "-hwaccel videotoolbox ")]
  [InlineData("none", "")]
  [InlineData("", "")]
  [InlineData(null, "")]
  [InlineData("bogus", "")]
  public void HwAccelArg_MapsModeToFfmpegOption(string? mode, string expected)
  {
    Assert.Equal(expected, SegmentDetection.HwAccelArg(mode));
  }

  [Fact]
  public void BuildOutroAnalyzeArgs_Auto_InsertsHwAccelBeforeInput()
  {
    Assert.Equal(
      "-hide_banner -nostats -hwaccel auto -ss 1234.5 -i \"/media/movie.mkv\" -vf fps=1,signalstats,metadata=print -af silencedetect=noise=-45dB:d=0.8 -f null -",
      SegmentDetection.BuildOutroAnalyzeArgs("auto", 1234.5, "/media/movie.mkv"));
  }

  [Fact]
  public void BuildOutroAnalyzeArgs_Cpu_HasNoHwAccel()
  {
    Assert.Equal(
      "-hide_banner -nostats -ss 60 -i \"/x.mp4\" -vf fps=1,signalstats,metadata=print -af silencedetect=noise=-45dB:d=0.8 -f null -",
      SegmentDetection.BuildOutroAnalyzeArgs("none", 60, "/x.mp4"));
  }

  // ----- Parsing -----

  [Fact]
  public void ParseRegions_ExtractsSilence()
  {
    const string output =
      "[Parsed_silencedetect_0 @ 0x1] silence_start: 15.010658\n" +
      "[Parsed_silencedetect_0 @ 0x1] silence_end: 20.03873 | silence_duration: 5.028073\n";

    var (_, silence) = SegmentDetection.ParseRegions(output, fallbackEnd: 20);

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
  public void ParseLumaSamples_PairsPtsTimeWithYavg()
  {
    const string output =
      "[Parsed_metadata_2 @ 0x0] frame:0    pts:0     pts_time:0\n" +
      "[Parsed_metadata_2 @ 0x0] lavfi.signalstats.YAVG=384.637\n" +
      "[Parsed_metadata_2 @ 0x0] frame:1    pts:1000  pts_time:1\n" +
      "[Parsed_metadata_2 @ 0x0] lavfi.signalstats.YAVG=72.5\n";

    var samples = SegmentDetection.ParseLumaSamples(output);

    Assert.Equal(2, samples.Count);
    Assert.Equal(0, samples[0].TimeSeconds, 3);
    Assert.Equal(384.637, samples[0].Value, 3);
    Assert.Equal(1, samples[1].TimeSeconds, 3);
    Assert.Equal(72.5, samples[1].Value, 3);
  }

  [Fact]
  public void ParseLumaSamples_ReturnsEmpty_ForNoMatches()
  {
    Assert.Empty(SegmentDetection.ParseLumaSamples("nothing here"));
    Assert.Empty(SegmentDetection.ParseLumaSamples(string.Empty));
  }

  // ----- Segmenter -----

  [Fact]
  public void DetectOutro_DarkCardThenBrightBonus_StopsAtTheBonus()
  {
    // Rick & Morty shape: 240s content, a 30s dark credit card, then a 30s bright bonus to the end.
    // window 300s, offset 1055 → runtime 1355. Dark=60, bright=500.
    var luma = Luma(300, s => (s >= 240 && s < 270) ? 60 : 500);

    var segs = SegmentDetection.DetectOutroSegments(luma, NoSilence, offsetSeconds: 1055, runtimeSeconds: 1355, Options());

    var seg = Assert.Single(segs);
    Assert.Equal(1295, seg.Start, 0);        // 1055 + 240 (card start)
    Assert.Equal(1325, seg.End, 0);          // 1055 + 270 (bonus start) — NOT runtime
    Assert.True(1355 - seg.End >= 15, "the post-credits bonus must be left un-skipped");
  }

  [Fact]
  public void DetectOutro_DarkCreditsToEnd_CoversToRuntime()
  {
    // Film shape: credits on black run to the very end (no bonus). window 720s, offset 6921 → runtime 7641.
    var luma = Luma(720, s => s < 151 ? 500 : 60);

    var segs = SegmentDetection.DetectOutroSegments(luma, NoSilence, 6921, 7641, Options());

    var seg = Assert.Single(segs);
    Assert.Equal(7072, seg.Start, 0);        // 6921 + 151
    Assert.Equal(7641, seg.End, 0);          // to runtime
  }

  [Fact]
  public void DetectOutro_SilentBrightCredits_AnchorViaSilence()
  {
    // Episode with a bright but silent end card (animated ED): luma stays high, a 97s silence runs to end.
    var luma = Luma(360, _ => 500);
    var silence = new[] { new DetectedRegion(263, 360) };

    var segs = SegmentDetection.DetectOutroSegments(luma, silence, 1440, 1800, Options());

    var seg = Assert.Single(segs);
    Assert.Equal(1703, seg.Start, 0);        // 1440 + 263
    Assert.Equal(1800, seg.End, 0);
  }

  [Fact]
  public void DetectOutro_MidCreditsBonus_ProducesTwoSegments()
  {
    // credits1 (30s) | bonus (30s) | credits2 (30s) | short bright tail. window 300s, offset 1055.
    var luma = Luma(300, s =>
    {
      if (s >= 200 && s < 230) return 60; // credits1
      if (s >= 260 && s < 290) return 60; // credits2
      return 500;                         // content / bonus / tail
    });

    var segs = SegmentDetection.DetectOutroSegments(luma, NoSilence, 1055, 1355, Options());

    Assert.Equal(2, segs.Count);
    Assert.Equal(1255, segs[0].Start, 0);
    Assert.Equal(1285, segs[0].End, 0);
    Assert.Equal(1315, segs[1].Start, 0);
    Assert.Equal(1345, segs[1].End, 0);
    Assert.True(segs[1].Start - segs[0].End >= 15, "the mid-credits bonus gap must be preserved between segments");
  }

  [Fact]
  public void DetectOutro_ShortBrightBlipInCredits_IsBridged()
  {
    // A 5s studio card inside an otherwise 60s dark credits block must not split the segment.
    var luma = Luma(300, s =>
    {
      if (s >= 220 && s < 280)
      {
        return (s >= 245 && s < 250) ? 500 : 60; // 5s bright blip inside the dark block
      }

      return 500;
    });

    var segs = SegmentDetection.DetectOutroSegments(luma, NoSilence, 1055, 1355, Options());

    var seg = Assert.Single(segs);
    Assert.Equal(1275, seg.Start, 0); // 1055 + 220, one contiguous block
    Assert.Equal(1335, seg.End, 0);   // 1055 + 280
  }

  [Fact]
  public void DetectOutro_DarkSceneFollowedByLongContent_IsRejected()
  {
    // A 31s dark scene early in the tail, then 269s of content to the end → not credits.
    var luma = Luma(300, s => s < 31 ? 60 : 500);

    Assert.Empty(SegmentDetection.DetectOutroSegments(luma, NoSilence, 1055, 1355, Options()));
  }

  [Fact]
  public void DetectOutro_TooShortDarkRun_IsIgnored()
  {
    // A 10s dark blip (< MinCreditRun) is not credits.
    var luma = Luma(300, s => (s >= 285 && s < 295) ? 60 : 500);

    Assert.Empty(SegmentDetection.DetectOutroSegments(luma, NoSilence, 1055, 1355, Options()));
  }

  [Fact]
  public void DetectOutro_ReturnsEmpty_ForNoSignals()
  {
    Assert.Empty(SegmentDetection.DetectOutroSegments(Array.Empty<LumaSample>(), NoSilence, 1055, 1355, Options()));
  }

  [Fact]
  public void OutroOptionsSignature_IsStable_ForTheSameTuning()
  {
    Assert.Equal(
      SegmentDetection.OutroOptionsSignature(Options(), 720),
      SegmentDetection.OutroOptionsSignature(Options(), 720));
  }

  [Fact]
  public void OutroOptionsSignature_Changes_WhenATuningValueChanges()
  {
    var baseline = SegmentDetection.OutroOptionsSignature(Options(), 720);
    var tweaked = Options() with { DarkFraction = 0.30 };

    Assert.NotEqual(baseline, SegmentDetection.OutroOptionsSignature(tweaked, 720));
  }

  [Fact]
  public void OutroOptionsSignature_Changes_WhenTheAnalysisWindowCapChanges()
  {
    // OutroAnalyzeMaxSeconds caps the analyzed tail, so it affects the result and must invalidate the cache.
    Assert.NotEqual(
      SegmentDetection.OutroOptionsSignature(Options(), 720),
      SegmentDetection.OutroOptionsSignature(Options(), 900));
  }
}
