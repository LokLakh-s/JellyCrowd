namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The adaptive-quota tier a user currently sits at. Each tier maps to a configurable percentage of the
/// user's base quota (the per-user override, or the global default): <see cref="Floor"/> and
/// <see cref="Ceiling"/> are configurable, <see cref="Base"/> is always 100% of the base quota.
/// </summary>
public enum AdaptiveTier
{
  /// <summary>Resting tier for chronically inactive users (a configurable percentage below the base, e.g. 40%).</summary>
  Floor,

  /// <summary>Neutral tier: exactly the user's base quota (100%). The starting tier for every user.</summary>
  Base,

  /// <summary>Reward tier for active users (a configurable percentage above the base, e.g. 200%).</summary>
  Ceiling,
}
