namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// A detected time range (in seconds, relative to the analyzed input) from ffmpeg black/silence output.
/// </summary>
/// <param name="Start">Start time in seconds.</param>
/// <param name="End">End time in seconds.</param>
public readonly record struct DetectedRegion(double Start, double End)
{
  /// <summary>Gets the region duration in seconds.</summary>
  public double Duration => End - Start;
}
