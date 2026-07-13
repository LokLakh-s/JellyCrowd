using Jellyfin.Plugin.JellyCrowd.Tasks;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Tasks;

/// <summary>
/// Tests for <see cref="OutroAnalysisTask"/>'s frame→time mapping, which is what places the Skip Outro
/// button. Fingerprint frames are relative to the episode's TAIL window, not to the start of the file.
/// </summary>
public class OutroAnalysisTaskTests
{
  private const long TicksPerSecond = 10_000_000;

  [Fact]
  public void ToAbsoluteRegion_ShiftsFramesByTheWindowOffset()
  {
    // A 24-minute episode, tail window of the last 300s → the window opens at 1140s. 300 frames over
    // 300s = 1 frame per second, so the shared run [60..119] is 1200s..1260s in the FILE.
    var region = OutroAnalysisTask.ToAbsoluteRegion(offsetSeconds: 1140, windowSeconds: 300, frameCount: 300, startFrame: 60, endFrame: 119);

    Assert.Equal(1200 * TicksPerSecond, region.StartTicks);
    Assert.Equal(1260 * TicksPerSecond, region.EndTicks); // end frame is inclusive
  }

  [Fact]
  public void ToAbsoluteRegion_FrameZero_IsTheStartOfTheWindow_NotOfTheFile()
  {
    // The bug this guards: treating frame 0 as t=0 would put the credits at the very beginning.
    var region = OutroAnalysisTask.ToAbsoluteRegion(offsetSeconds: 900, windowSeconds: 300, frameCount: 300, startFrame: 0, endFrame: 0);

    Assert.Equal(900 * TicksPerSecond, region.StartTicks);
    Assert.Equal(901 * TicksPerSecond, region.EndTicks);
  }

  [Fact]
  public void ToAbsoluteRegion_ScalesWhenFramesAreDenserThanOneASecond()
  {
    // Chromaprint emits ~8 frames a second; the mapping must divide by the real frame rate, not assume 1.
    var region = OutroAnalysisTask.ToAbsoluteRegion(offsetSeconds: 100, windowSeconds: 300, frameCount: 2400, startFrame: 800, endFrame: 1599);

    Assert.Equal(200 * TicksPerSecond, region.StartTicks);  // 100 + 800/8
    Assert.Equal(300 * TicksPerSecond, region.EndTicks);    // 100 + 1600/8
  }

  [Fact]
  public void ToAbsoluteRegion_ShortEpisode_WindowStartsAtZero()
  {
    // An episode shorter than the window: the tail IS the whole file, so offsets are zero.
    var region = OutroAnalysisTask.ToAbsoluteRegion(offsetSeconds: 0, windowSeconds: 300, frameCount: 300, startFrame: 250, endFrame: 299);

    Assert.Equal(250 * TicksPerSecond, region.StartTicks);
    Assert.Equal(300 * TicksPerSecond, region.EndTicks);
  }
}
