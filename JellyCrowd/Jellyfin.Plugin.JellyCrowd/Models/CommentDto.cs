namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Payload to post a comment on a catalog title.
/// </summary>
public class CommentDto
{
  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>).</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the comment text.</summary>
  public string Text { get; set; } = string.Empty;
}
