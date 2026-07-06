namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// A cached outro (end-credits) segment, in Jellyfin ticks, at its absolute position within the item.
/// </summary>
/// <param name="StartTicks">Outro start, in ticks.</param>
/// <param name="EndTicks">Outro end, in ticks.</param>
public sealed record OutroRegion(long StartTicks, long EndTicks);
