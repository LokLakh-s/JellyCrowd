namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Admin payload to set (or clear) the header announcement banner.
/// </summary>
public class AnnouncementDto
{
  /// <summary>
  /// Gets or sets the announcement text. Empty clears the banner.
  /// </summary>
  public string Text { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the severity colour: <c>green</c>, <c>yellow</c> or <c>red</c>.
  /// </summary>
  public string Level { get; set; } = "green";
}
