namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Total watch minutes recorded for a user on a single UTC day. The aggregate unit behind both
/// adaptive-quota signals: volume (sum of minutes) and regularity (number of distinct days).
/// </summary>
public class DailyWatch
{
  /// <summary>
  /// Gets or sets the UTC day in <c>yyyy-MM-dd</c> format.
  /// </summary>
  public string Date { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the total minutes watched on that day.
  /// </summary>
  public double Minutes { get; set; }
}
