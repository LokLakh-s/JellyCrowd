using System;
using System.Globalization;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure (network- and IO-free) core of the adaptive quota. Splits two concerns so reads stay cheap and
/// state transitions stay deterministic:
/// <list type="bullet">
/// <item><description><see cref="ComputeBytes"/> — turns a persisted tier/probation state into an effective
/// quota in bytes, scaled off the user's current base quota. Called on every quota read; never mutates.</description></item>
/// <item><description><see cref="Evaluate"/> — the hysteresis state machine. Given the activity window it
/// decides the next tier and probation state. Called periodically by a background evaluator.</description></item>
/// </list>
/// Tiers: active users rise straight to the ceiling; a rewarded user who goes quiet is put on probation
/// (their quota frozen) rather than dropped — failing probation lands them at the base, not the floor. Only
/// users who were never rewarded decay to the floor.
/// </summary>
public static class AdaptiveQuotaCalculator
{
  /// <summary>A day must reach this many minutes to count toward the "regularity" (distinct active days) signal.</summary>
  private const double MinMinutesForActiveDay = 5d;

  /// <summary>
  /// Computes the effective quota in bytes for a user from their persisted tier/probation state, scaled off
  /// <paramref name="baseBytes"/> (the user's override-or-default quota). Returns <paramref name="baseBytes"/>
  /// unchanged when the feature is off or the base is unlimited.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="baseBytes">The user's base quota in bytes (0 means unlimited).</param>
  /// <param name="activity">The user's activity/tier state.</param>
  /// <returns>The effective quota in bytes.</returns>
  public static long ComputeBytes(PluginConfiguration config, long baseBytes, UserActivity activity)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(activity);

    if (!config.AdaptiveQuotaEnabled || baseBytes <= 0)
    {
      return baseBytes;
    }

    if (activity.ProbationStartUtc is not null && activity.FrozenQuotaBytes is { } frozen)
    {
      return frozen < 0 ? 0 : frozen;
    }

    return BytesForTier(config, baseBytes, activity.Tier);
  }

  /// <summary>
  /// Computes the quota in bytes for a specific tier, scaled off <paramref name="baseBytes"/>.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="baseBytes">The base quota in bytes.</param>
  /// <param name="tier">The tier.</param>
  /// <returns>The quota in bytes for that tier.</returns>
  public static long BytesForTier(PluginConfiguration config, long baseBytes, AdaptiveTier tier)
  {
    ArgumentNullException.ThrowIfNull(config);
    var percent = tier switch
    {
      AdaptiveTier.Floor => Math.Max(0, config.AdaptiveFloorPercent),
      AdaptiveTier.Ceiling => Math.Max(0, config.AdaptiveCeilingPercent),
      _ => 100,
    };

    // Scale via decimal to avoid long overflow on large quotas × large percentages.
    return (long)(baseBytes / 100m * percent);
  }

  /// <summary>
  /// Returns whether a user counts as "active": both the volume (minutes) and regularity (distinct days)
  /// thresholds are met within the rolling window ending at <paramref name="nowUtc"/>.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="activity">The user's activity.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <returns><c>true</c> when the user is active.</returns>
  public static bool IsActive(PluginConfiguration config, UserActivity activity, DateTime nowUtc)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(activity);

    var windowDays = Math.Max(1, config.AdaptiveWindowDays);
    var cutoff = nowUtc.Date.AddDays(-(windowDays - 1));

    double minutes = 0;
    var activeDays = 0;
    foreach (var day in activity.Days)
    {
      if (!DateTime.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d < cutoff)
      {
        continue;
      }

      minutes += day.Minutes;
      if (day.Minutes >= MinMinutesForActiveDay)
      {
        activeDays++;
      }
    }

    return minutes >= config.AdaptiveMinMinutes && activeDays >= config.AdaptiveMinActiveDays;
  }

  /// <summary>
  /// Runs the hysteresis state machine for one user and returns the next tier/probation state plus the
  /// transition that occurred (so the caller can persist and notify). Does not mutate <paramref name="activity"/>.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="activity">The user's current activity/tier state.</param>
  /// <param name="baseBytes">The user's base quota in bytes (used when freezing a probation amount).</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <returns>The transition decision.</returns>
  public static AdaptiveTransition Evaluate(PluginConfiguration config, UserActivity activity, long baseBytes, DateTime nowUtc)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(activity);

    var unchanged = new AdaptiveTransition
    {
      Tier = activity.Tier,
      ProbationStartUtc = activity.ProbationStartUtc,
      FrozenQuotaBytes = activity.FrozenQuotaBytes,
      Event = AdaptiveEvent.None,
    };

    if (!config.AdaptiveQuotaEnabled || baseBytes <= 0)
    {
      return unchanged;
    }

    var active = IsActive(config, activity, nowUtc);
    var onProbation = activity.ProbationStartUtc is not null;
    var gapDays = (nowUtc - activity.LastSeenUtc).TotalDays;
    var inactivityDays = Math.Max(1, config.AdaptiveInactivityDays);

    if (active)
    {
      // Rise fast: any active user goes (or returns) to the ceiling and clears probation.
      var restored = onProbation;
      var promoted = activity.Tier != AdaptiveTier.Ceiling;
      return new AdaptiveTransition
      {
        Tier = AdaptiveTier.Ceiling,
        ProbationStartUtc = null,
        FrozenQuotaBytes = null,
        Event = restored ? AdaptiveEvent.ProbationPassed : (promoted ? AdaptiveEvent.Promoted : AdaptiveEvent.None),
      };
    }

    if (onProbation)
    {
      var probationEnd = activity.ProbationStartUtc!.Value.AddDays(Math.Max(1, config.AdaptiveProbationDays));
      if (nowUtc >= probationEnd)
      {
        // Reward for past activity: a failed probation lands at the base quota, not the floor.
        return new AdaptiveTransition
        {
          Tier = AdaptiveTier.Base,
          ProbationStartUtc = null,
          FrozenQuotaBytes = null,
          Event = AdaptiveEvent.ProbationFailed,
        };
      }

      return unchanged; // probation still running; frozen quota holds.
    }

    if (gapDays > inactivityDays)
    {
      if (activity.Tier == AdaptiveTier.Ceiling)
      {
        // Put the rewarded-but-now-quiet user on notice, freezing their current (ceiling) quota.
        return new AdaptiveTransition
        {
          Tier = AdaptiveTier.Ceiling,
          ProbationStartUtc = nowUtc,
          FrozenQuotaBytes = BytesForTier(config, baseBytes, AdaptiveTier.Ceiling),
          Event = AdaptiveEvent.ProbationStarted,
        };
      }

      if (activity.Tier == AdaptiveTier.Base)
      {
        // Never rewarded and persistently inactive: decay to the floor.
        return new AdaptiveTransition
        {
          Tier = AdaptiveTier.Floor,
          ProbationStartUtc = null,
          FrozenQuotaBytes = null,
          Event = AdaptiveEvent.DecayedToFloor,
        };
      }
    }

    return unchanged;
  }
}
