namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// What an ownership change did, counted per member and media.
/// </summary>
public class OwnershipChangeResult
{
  /// <summary>
  /// Gets or sets how many ownerships were created.
  /// </summary>
  public int Given { get; set; }

  /// <summary>
  /// Gets or sets how many were already held (and renewed when given).
  /// </summary>
  public int AlreadyOwned { get; set; }

  /// <summary>
  /// Gets or sets how many ownerships were removed.
  /// </summary>
  public int Removed { get; set; }

  /// <summary>
  /// Gets or sets how many removals found nothing to remove.
  /// </summary>
  public int NotOwned { get; set; }

  /// <summary>
  /// Gets or sets how many media could not be given (not in the library, or invalid).
  /// </summary>
  public int Failed { get; set; }
}
