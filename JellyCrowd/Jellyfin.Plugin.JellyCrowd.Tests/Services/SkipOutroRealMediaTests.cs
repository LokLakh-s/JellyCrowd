using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// End-to-end regression tests for the Skip Outro segmenter, run against real jellyfin-ffmpeg output
/// captured from Rick &amp; Morty S09 — episodes that end with a dark credit card followed by a bright
/// post-credits bonus scene. The whole point: the emitted outro must stop at the bonus, never skip it.
/// </summary>
public class SkipOutroRealMediaTests
{
  private static readonly OutroDetectionOptions DefaultOptions = new(
    DarkFraction: 0.25,
    MinCreditRunSeconds: 15,
    MinBonusRunSeconds: 10,
    MaxBonusGapSeconds: 90,
    MaxTrailingBonusSeconds: 150,
    MinTrailingSilenceSeconds: 20,
    SilenceEndToleranceSeconds: 15,
    MinCreditsSeconds: 20,
    MaxCreditsSeconds: 900);

  [Theory]
  [InlineData("rm_s09e01.txt")]
  [InlineData("rm_s09e02.txt")]
  [InlineData("rm_s09e03.txt")]
  [InlineData("rm_s09e04.txt")]
  [InlineData("rm_s09e05.txt")]
  [InlineData("rm_s09e06.txt")]
  public void Detects_credits_and_preserves_the_post_credits_bonus(string fixture)
  {
    var (runtime, ffmpegText) = LoadFixture(fixture);

    // Same window as the provider: last 20% of the runtime, clamped to [120, 720] seconds.
    var window = Math.Clamp(runtime * 0.20, 120, 720);
    var offset = Math.Max(0, runtime - window);
    var analyzed = runtime - offset;

    var (_, silence) = SegmentDetection.ParseRegions(ffmpegText, analyzed);
    var luma = SegmentDetection.ParseLumaSamples(ffmpegText);

    var segments = SegmentDetection.DetectOutroSegments(luma, silence, offset, runtime, DefaultOptions);

    Assert.NotEmpty(segments);
    Assert.InRange(segments.Count, 1, 2);

    var first = segments[0];
    var last = segments[^1];

    // Credits found near the end of the episode.
    Assert.InRange(runtime - first.Start, 20, 220);

    // The bonus is left un-skipped: the outro ends well before the file end.
    var bonusSeconds = runtime - last.End;
    Assert.True(
      bonusSeconds >= 12,
      $"{fixture}: outro ends {bonusSeconds:0}s before the end — the post-credits bonus would be skipped.");
    Assert.True(
      bonusSeconds <= 150,
      $"{fixture}: {bonusSeconds:0}s left after the outro — too much to be a bonus.");

    // Every segment is a real, ordered range.
    foreach (var s in segments)
    {
      Assert.True(s.End > s.Start);
      Assert.True(s.End <= runtime);
    }
  }

  private static (double Runtime, string FfmpegText) LoadFixture(string fixture)
  {
    var path = Path.Combine(AppContext.BaseDirectory, "TestData", "SkipOutro", fixture);
    var lines = File.ReadAllLines(path);

    var runtimeLine = lines[0];
    Assert.StartsWith("# runtime=", runtimeLine);
    var runtime = double.Parse(runtimeLine.AsSpan("# runtime=".Length), CultureInfo.InvariantCulture);

    var text = string.Join('\n', lines.Skip(1));
    return (runtime, text);
  }
}
