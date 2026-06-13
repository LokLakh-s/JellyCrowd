namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Rolling window used to rate-limit how many requests a user may make.
/// </summary>
public enum RequestPeriod
{
  /// <summary>
  /// A rolling 24-hour window.
  /// </summary>
  Day,

  /// <summary>
  /// A rolling 7-day window.
  /// </summary>
  Week,

  /// <summary>
  /// A rolling 30-day window.
  /// </summary>
  Month
}
