namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A single billed cast member of a movie/show, surfaced on the catalog detail popup.
/// </summary>
public class CastMember
{
  /// <summary>Gets or sets the actor's name.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>Gets or sets the character they play (may be empty).</summary>
  public string Character { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB profile image path (may be <c>null</c>).</summary>
  public string? ProfilePath { get; set; }
}
