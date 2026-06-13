namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A TMDB genre.
/// </summary>
public class Genre
{
  /// <summary>
  /// Gets or sets the TMDB genre identifier.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  /// Gets or sets the localized genre name.
  /// </summary>
  public string Name { get; set; } = string.Empty;
}
