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

  /// <summary>Gets or sets the review text (optional when a rating is given).</summary>
  public string Text { get; set; } = string.Empty;

  /// <summary>Gets or sets the rating on a 1–10 scale (required for a review).</summary>
  public int Rating { get; set; }
}
