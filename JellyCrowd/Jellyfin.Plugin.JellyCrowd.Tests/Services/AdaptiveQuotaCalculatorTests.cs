using System;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for the pure <see cref="AdaptiveQuotaCalculator"/> (byte scaling + hysteresis state machine).
/// </summary>
public sealed class AdaptiveQuotaCalculatorTests
{
  private const long Gib = 1024L * 1024 * 1024;
  private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

  private static PluginConfiguration Config() => new()
  {
    AdaptiveQuotaEnabled = true,
    AdaptiveFloorPercent = 40,
    AdaptiveCeilingPercent = 200,
    AdaptiveWindowDays = 14,
    AdaptiveMinMinutes = 180,
    AdaptiveMinActiveDays = 3,
    AdaptiveInactivityDays = 30,
    AdaptiveProbationDays = 14,
  };

  private static UserActivity WithDays(UserActivity activity, int days, double minutesEach, DateTime endingUtc)
  {
    for (var i = 0; i < days; i++)
    {
      activity.Days.Add(new DailyWatch
      {
        Date = endingUtc.Date.AddDays(-i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        Minutes = minutesEach,
      });
    }

    activity.LastSeenUtc = endingUtc;
    return activity;
  }

  [Fact]
  public void ComputeBytes_ScalesOffBasePerTier()
  {
    var config = Config();
    Assert.Equal(40 * Gib, AdaptiveQuotaCalculator.BytesForTier(config, 100 * Gib, AdaptiveTier.Floor));
    Assert.Equal(100 * Gib, AdaptiveQuotaCalculator.BytesForTier(config, 100 * Gib, AdaptiveTier.Base));
    Assert.Equal(200 * Gib, AdaptiveQuotaCalculator.BytesForTier(config, 100 * Gib, AdaptiveTier.Ceiling));
  }

  [Fact]
  public void ComputeBytes_DisabledReturnsBase()
  {
    var config = Config();
    config.AdaptiveQuotaEnabled = false;
    var activity = new UserActivity { Tier = AdaptiveTier.Ceiling };
    Assert.Equal(50 * Gib, AdaptiveQuotaCalculator.ComputeBytes(config, 50 * Gib, activity));
  }

  [Fact]
  public void ComputeBytes_UnlimitedBaseStaysUnlimited()
  {
    var activity = new UserActivity { Tier = AdaptiveTier.Ceiling };
    Assert.Equal(0, AdaptiveQuotaCalculator.ComputeBytes(Config(), 0, activity));
  }

  [Fact]
  public void ComputeBytes_ProbationReturnsFrozen()
  {
    var activity = new UserActivity
    {
      Tier = AdaptiveTier.Ceiling,
      ProbationStartUtc = Now,
      FrozenQuotaBytes = 77 * Gib,
    };
    Assert.Equal(77 * Gib, AdaptiveQuotaCalculator.ComputeBytes(Config(), 50 * Gib, activity));
  }

  [Fact]
  public void Evaluate_ActiveUserRisesToCeiling()
  {
    var activity = WithDays(new UserActivity { Tier = AdaptiveTier.Base }, days: 5, minutesEach: 60, endingUtc: Now);
    var t = AdaptiveQuotaCalculator.Evaluate(Config(), activity, 50 * Gib, Now);
    Assert.Equal(AdaptiveTier.Ceiling, t.Tier);
    Assert.Equal(AdaptiveEvent.Promoted, t.Event);
  }

  [Fact]
  public void Evaluate_BelowVolumeOrRegularityIsNotActive()
  {
    // 5 days but only 30 min each = 150 < 180 min volume.
    var lowVolume = WithDays(new UserActivity { Tier = AdaptiveTier.Base }, days: 5, minutesEach: 30, endingUtc: Now);
    Assert.False(AdaptiveQuotaCalculator.IsActive(Config(), lowVolume, Now));

    // Plenty of minutes but on only 2 days < 3.
    var fewDays = WithDays(new UserActivity { Tier = AdaptiveTier.Base }, days: 2, minutesEach: 200, endingUtc: Now);
    Assert.False(AdaptiveQuotaCalculator.IsActive(Config(), fewDays, Now));
  }

  [Fact]
  public void Evaluate_RewardedUserGoesQuiet_StartsProbationFreezingCeiling()
  {
    var activity = new UserActivity { Tier = AdaptiveTier.Ceiling, LastSeenUtc = Now.AddDays(-40) };
    var t = AdaptiveQuotaCalculator.Evaluate(Config(), activity, 50 * Gib, Now);
    Assert.Equal(AdaptiveEvent.ProbationStarted, t.Event);
    Assert.Equal(Now, t.ProbationStartUtc);
    Assert.Equal(100 * Gib, t.FrozenQuotaBytes); // 200% of 50 GiB
  }

  [Fact]
  public void Evaluate_ProbationElapsedWithoutActivity_DropsToBaseNotFloor()
  {
    var activity = new UserActivity
    {
      Tier = AdaptiveTier.Ceiling,
      LastSeenUtc = Now.AddDays(-60),
      ProbationStartUtc = Now.AddDays(-15), // 15 > 14 days probation
      FrozenQuotaBytes = 100 * Gib,
    };
    var t = AdaptiveQuotaCalculator.Evaluate(Config(), activity, 50 * Gib, Now);
    Assert.Equal(AdaptiveTier.Base, t.Tier);
    Assert.Equal(AdaptiveEvent.ProbationFailed, t.Event);
    Assert.Null(t.ProbationStartUtc);
  }

  [Fact]
  public void Evaluate_BecomingActiveDuringProbation_RestoresCeiling()
  {
    var activity = WithDays(
      new UserActivity { Tier = AdaptiveTier.Ceiling, ProbationStartUtc = Now.AddDays(-3), FrozenQuotaBytes = 100 * Gib },
      days: 4,
      minutesEach: 60,
      endingUtc: Now);
    var t = AdaptiveQuotaCalculator.Evaluate(Config(), activity, 50 * Gib, Now);
    Assert.Equal(AdaptiveTier.Ceiling, t.Tier);
    Assert.Equal(AdaptiveEvent.ProbationPassed, t.Event);
    Assert.Null(t.ProbationStartUtc);
  }

  [Fact]
  public void Evaluate_NeverRewardedInactiveUser_DecaysToFloor()
  {
    var activity = new UserActivity { Tier = AdaptiveTier.Base, LastSeenUtc = Now.AddDays(-40) };
    var t = AdaptiveQuotaCalculator.Evaluate(Config(), activity, 50 * Gib, Now);
    Assert.Equal(AdaptiveTier.Floor, t.Tier);
    Assert.Equal(AdaptiveEvent.DecayedToFloor, t.Event);
  }

  [Fact]
  public void Evaluate_DisabledIsNoOp()
  {
    var config = Config();
    config.AdaptiveQuotaEnabled = false;
    var activity = new UserActivity { Tier = AdaptiveTier.Ceiling, LastSeenUtc = Now.AddDays(-90) };
    var t = AdaptiveQuotaCalculator.Evaluate(config, activity, 50 * Gib, Now);
    Assert.False(t.Changed);
  }
}
