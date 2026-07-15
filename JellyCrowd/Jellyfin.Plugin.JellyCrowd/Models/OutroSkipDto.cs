namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The end-credits region of a playing item, plus its runtime, so the web client can decide what the
/// Skip Outro control should do (advance to the next episode, or jump to a post-credits bonus). The
/// decision itself lives in one tested place — <c>catalog.lib.js</c>'s <c>outroSkipPlan</c>.
/// </summary>
public sealed class OutroSkipDto
{
  /// <summary>Gets or sets the start of the end-credits, in Jellyfin ticks.</summary>
  public long OutroStartTicks { get; set; }

  /// <summary>Gets or sets the end of the end-credits, in Jellyfin ticks.</summary>
  public long OutroEndTicks { get; set; }

  /// <summary>Gets or sets the item's runtime, in Jellyfin ticks.</summary>
  public long RunTimeTicks { get; set; }
}
