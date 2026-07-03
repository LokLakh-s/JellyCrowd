namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// One average-luma (brightness) sample taken from the ffmpeg <c>signalstats</c> filter, used to tell
/// dark end-credits apart from a bright post-credits bonus scene.
/// </summary>
/// <param name="TimeSeconds">Sample time in seconds, relative to the analyzed window.</param>
/// <param name="Value">Average luma (<c>YAVG</c>); scale depends on bit depth, so it is only ever
/// compared against a reference derived from the same clip, never an absolute constant.</param>
public readonly record struct LumaSample(double TimeSeconds, double Value);
