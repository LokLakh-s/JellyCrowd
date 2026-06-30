namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>One day in the activity-over-time series.</summary>
public class StatsDayDto
{
  /// <summary>Gets or sets the UTC day (ISO <c>yyyy-MM-dd</c>).</summary>
  public string Date { get; set; } = string.Empty;

  /// <summary>Gets or sets the number of plays that started on the day.</summary>
  public int Plays { get; set; }

  /// <summary>Gets or sets the watched minutes on the day.</summary>
  public double Minutes { get; set; }
}
