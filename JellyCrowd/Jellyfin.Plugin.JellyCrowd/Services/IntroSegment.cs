namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// A detected episode intro, in Jellyfin ticks, cached by the intro-analysis task and served to the native
/// media-segment provider.
/// </summary>
/// <param name="StartTicks">Intro start, in ticks.</param>
/// <param name="EndTicks">Intro end, in ticks.</param>
public sealed record IntroSegment(long StartTicks, long EndTicks);
