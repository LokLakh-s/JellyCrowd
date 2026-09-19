namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Payload to report an issue, either about a title or standing on its own (leave the media fields empty).
/// </summary>
public class ReportDto
{
  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>); empty reports a general issue.</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier (ignored without a media type).</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the title (ignored without a media type).</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the issue description.</summary>
  public string Message { get; set; } = string.Empty;

  /// <summary>Gets or sets the issue category (bug / subtitles / audio / quality / playback / account / other).</summary>
  public string Type { get; set; } = "other";
}
