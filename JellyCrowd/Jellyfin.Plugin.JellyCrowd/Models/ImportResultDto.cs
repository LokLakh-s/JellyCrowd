namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The outcome of importing historical playback from the Playback Reporting plugin's database.
/// </summary>
public class ImportResultDto
{
  /// <summary>Gets or sets a value indicating whether a Playback Reporting database was found.</summary>
  public bool Found { get; set; }

  /// <summary>Gets or sets the number of rows read from the Playback Reporting database.</summary>
  public int Scanned { get; set; }

  /// <summary>Gets or sets the number of records actually imported into the history (after dedup and retention).</summary>
  public int Imported { get; set; }
}
