using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The Local Intros settings the web client's injected script needs to force Cinema Mode and enforce a
/// non-skippable pre-roll.
/// </summary>
public class LocalIntrosSettingsDto
{
  /// <summary>Gets or sets a value indicating whether local pre-rolls are enabled.</summary>
  public bool Enabled { get; set; }

  /// <summary>Gets or sets a value indicating whether the pre-roll must be non-skippable (web client).</summary>
  public bool NonSkippable { get; set; }

  /// <summary>Gets or sets a value indicating whether the client's Cinema Mode should be force-enabled.</summary>
  public bool ForceCinemaMode { get; set; }

  /// <summary>
  /// Gets the pre-roll item ids (dash-less "N" form) so the script can recognise a pre-roll while it plays.
  /// </summary>
  public IReadOnlyList<string> ItemIds { get; init; } = new List<string>();
}
