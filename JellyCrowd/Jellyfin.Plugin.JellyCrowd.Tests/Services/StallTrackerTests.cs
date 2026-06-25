using System;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="StallTracker"/> — the no-progress-over-threshold stall detector.
/// </summary>
public class StallTrackerTests
{
  private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
  private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(60);

  [Fact]
  public void NoProgressPastThreshold_Recovers_ThenResets()
  {
    var t = new StallTracker();
    Assert.False(t.ShouldRecover("a", 0, "downloading", T0, Threshold));                 // first sighting → start clock
    Assert.False(t.ShouldRecover("a", 0, "downloading", T0.AddMinutes(59), Threshold));  // still under threshold
    Assert.True(t.ShouldRecover("a", 0, "downloading", T0.AddMinutes(61), Threshold));   // stalled long enough → recover
    Assert.False(t.ShouldRecover("a", 0, "downloading", T0.AddMinutes(62), Threshold));  // cleared after recovering → clock restarts
  }

  [Fact]
  public void Progress_ResetsTheClock()
  {
    var t = new StallTracker();
    Assert.False(t.ShouldRecover("b", 10, "downloading", T0, Threshold));
    Assert.False(t.ShouldRecover("b", 20, "downloading", T0.AddMinutes(30), Threshold)); // progress → reset
    Assert.False(t.ShouldRecover("b", 20, "downloading", T0.AddMinutes(61), Threshold)); // only 31 min since progress
    Assert.True(t.ShouldRecover("b", 20, "downloading", T0.AddMinutes(91), Threshold));  // 61 min since progress → recover
  }

  [Fact]
  public void WarningState_StalledIsRecovered()
  {
    var t = new StallTracker();
    Assert.False(t.ShouldRecover("d", 0, "warning", T0, Threshold));
    Assert.True(t.ShouldRecover("d", 0, "warning", T0.AddMinutes(61), Threshold));
  }

  [Fact]
  public void NonCandidateStates_NeverRecover()
  {
    var t = new StallTracker();
    Assert.False(t.ShouldRecover("e", 0, "downloading", T0, Threshold)); // start tracking
    // Item finishes importing → not a candidate; must never recover and tracking is forgotten.
    Assert.False(t.ShouldRecover("e", 100, "importing", T0.AddMinutes(120), Threshold));
    Assert.False(t.ShouldRecover("e", 100, "completed", T0.AddMinutes(180), Threshold));
    Assert.False(t.ShouldRecover("f", 0, "missing", T0.AddMinutes(180), Threshold));
  }

  [Fact]
  public void CompletedDownload_IsNotStalled()
  {
    var t = new StallTracker();
    Assert.False(t.ShouldRecover("g", 100, "downloading", T0, Threshold));
    Assert.False(t.ShouldRecover("g", 100, "downloading", T0.AddMinutes(120), Threshold)); // 100% → never stalled
  }
}
