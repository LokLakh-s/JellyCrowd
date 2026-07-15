using System;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="PlaybackController"/> — the outro region the client uses to build a smart Skip
/// Outro control.
/// </summary>
public class PlaybackControllerTests
{
  private static readonly Guid Item = Guid.NewGuid();

  private static PlaybackController Create(
    OutroRegion? fingerprint = null,
    OutroAnalysis? heuristic = null,
    long runtimeTicks = 24 * 60 * 10_000_000L,
    bool enabled = true)
  {
    var fp = new Mock<IOutroSegmentStore>();
    fp.Setup(s => s.Get(Item)).Returns(fingerprint);
    var heur = new Mock<IOutroStore>();
    heur.Setup(s => s.Get(Item)).Returns(heuristic);

    var library = new Mock<ILibraryManager>();
    library.Setup(l => l.GetItemById(Item)).Returns(runtimeTicks > 0 ? new Movie { RunTimeTicks = runtimeTicks } : null);

    var config = new PluginConfiguration { SkipOutroEnabled = enabled };
    return new PlaybackController(fp.Object, heur.Object, library.Object, () => config);
  }

  [Fact]
  public void Outro_ReturnsTheFingerprintedRegionAndRuntime()
  {
    var controller = Create(fingerprint: new OutroRegion(1200 * 10_000_000L, 1290 * 10_000_000L));

    var dto = Assert.IsType<OutroSkipDto>(Assert.IsType<OkObjectResult>(controller.Outro(Item).Result).Value);

    Assert.Equal(1200 * 10_000_000L, dto.OutroStartTicks);
    Assert.Equal(1290 * 10_000_000L, dto.OutroEndTicks);
    Assert.Equal(24 * 60 * 10_000_000L, dto.RunTimeTicks);
  }

  [Fact]
  public void Outro_FeatureDisabled_IsNoContent()
  {
    Assert.IsType<NoContentResult>(Create(fingerprint: new OutroRegion(1, 2), enabled: false).Outro(Item).Result);
  }

  [Fact]
  public void Outro_NoRegion_IsNoContent()
  {
    Assert.IsType<NoContentResult>(Create().Outro(Item).Result);
  }

  [Fact]
  public void Outro_UnknownRuntime_IsNoContent()
  {
    // Without a runtime the client cannot tell "credits reach the end" from "bonus after" — so offer nothing.
    var controller = Create(fingerprint: new OutroRegion(100, 200), runtimeTicks: 0);

    Assert.IsType<NoContentResult>(controller.Outro(Item).Result);
  }
}
