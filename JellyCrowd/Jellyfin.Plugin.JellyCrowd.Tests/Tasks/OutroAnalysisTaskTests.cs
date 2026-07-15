using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using Jellyfin.Plugin.JellyCrowd.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Tasks;

/// <summary>
/// Tests for <see cref="OutroAnalysisTask"/>'s frame→time mapping, which is what places the Skip Outro
/// button, and its re-scan (coverage) logic.
/// </summary>
public class OutroAnalysisTaskTests
{
  private const long TicksPerSecond = 10_000_000;

  private static OutroRegion? Found() => new(1200 * TicksPerSecond, 1290 * TicksPerSecond);
  private static OutroRegion? None() => new(-1, -1); // "analyzed, no outro" sentinel

  [Theory]
  [InlineData(false)]  // covered season → skipped
  [InlineData(true)]   // one episode still uncovered → re-analyzed
  public async Task ReRun_RetriesUncoveredEpisodes_ButSkipsAFullyFoundSeason(bool oneUncovered)
  {
    var seasonId = Guid.NewGuid();
    var ep1 = new Episode { Id = Guid.NewGuid(), Path = "/tv/s01e01.mkv", RunTimeTicks = 1400 * TicksPerSecond, IndexNumber = 1 };
    var ep2 = new Episode { Id = Guid.NewGuid(), Path = "/tv/s01e02.mkv", RunTimeTicks = 1400 * TicksPerSecond, IndexNumber = 2 };

    var library = new Mock<ILibraryManager>();
    library.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Season))))
      .Returns(new List<BaseItem> { new Season { Id = seasonId } });
    library.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Episode))))
      .Returns(new List<BaseItem> { ep1, ep2 });

    var store = new Mock<IOutroSegmentStore>();
    store.Setup(s => s.Get(ep1.Id)).Returns(Found());
    store.Setup(s => s.Get(ep2.Id)).Returns(oneUncovered ? None() : Found());

    var extractor = new Mock<IFingerprintExtractor>();
    extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(Array.Empty<uint>());

    var config = new PluginConfiguration { SkipOutroEnabled = true };
    var task = new OutroAnalysisTask(library.Object, extractor.Object, store.Object, () => config, NullLogger<OutroAnalysisTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    // A NoOutro episode makes the whole season re-analyze (so the gap can be filled); a fully-found season
    // is skipped, so a routine re-run over a detected library costs nothing.
    extractor.Verify(
      e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
      oneUncovered ? Times.Exactly(2) : Times.Never());
  }

  [Fact]
  public void IsFoundOutro_TellsAFoundRegionApartFromTheNoneSentinelAndAbsent()
  {
    Assert.True(OutroAnalysisTask.IsFoundOutro(Found()));
    Assert.False(OutroAnalysisTask.IsFoundOutro(None()));   // analyzed, none → retry
    Assert.False(OutroAnalysisTask.IsFoundOutro(null));     // never analyzed → analyze
    Assert.False(OutroAnalysisTask.IsFoundOutro(new OutroRegion(50, 50))); // empty region → retry
  }

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
