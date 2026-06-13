namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A TMDB watch provider (streaming platform) available in a region.
/// </summary>
public class WatchProvider
{
  /// <summary>
  /// Gets or sets the TMDB provider id (used as <c>with_watch_providers</c>).
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  /// Gets or sets the provider display name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TMDB relative logo path.
  /// </summary>
  public string? LogoPath { get; set; }

  /// <summary>
  /// Gets or sets the regional display priority (lower is more prominent).
  /// </summary>
  public int DisplayPriority { get; set; }
}
