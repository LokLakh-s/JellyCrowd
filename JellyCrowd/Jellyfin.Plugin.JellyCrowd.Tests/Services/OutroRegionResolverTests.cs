using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="OutroRegionResolver"/> — which end-credits region is exposed, and its precedence.
/// </summary>
public class OutroRegionResolverTests
{
  private static OutroAnalysis Heuristic(params OutroRegion[] regions)
    => new(0, 0, 0, "sig", regions);

  [Fact]
  public void FingerprintedRegion_WinsOverTheHeuristic()
  {
    var fp = new OutroRegion(1200, 1290);
    var heur = Heuristic(new OutroRegion(1000, 1100));

    var region = OutroRegionResolver.Resolve(fp, heur);

    Assert.Equal(1200, region!.StartTicks);
    Assert.Equal(1290, region.EndTicks);
  }

  [Fact]
  public void FallsBackToHeuristic_WhenNoFingerprint()
  {
    var region = OutroRegionResolver.Resolve(null, Heuristic(new OutroRegion(1000, 1100)));

    Assert.Equal(1000, region!.StartTicks);
  }

  [Fact]
  public void FingerprintSentinel_IsNotARegion_FallsBackToHeuristic()
  {
    // A start below zero is "analyzed, no ED found" — not a region, so the heuristic gets its turn.
    var region = OutroRegionResolver.Resolve(new OutroRegion(-1, -1), Heuristic(new OutroRegion(1000, 1100)));

    Assert.Equal(1000, region!.StartTicks);
  }

  [Fact]
  public void EmptyEverywhere_IsNull()
  {
    Assert.Null(OutroRegionResolver.Resolve(null, null));
    Assert.Null(OutroRegionResolver.Resolve(new OutroRegion(-1, -1), Heuristic()));
    Assert.Null(OutroRegionResolver.Resolve(null, Heuristic(new OutroRegion(5, 5)))); // empty region ignored
  }
}
