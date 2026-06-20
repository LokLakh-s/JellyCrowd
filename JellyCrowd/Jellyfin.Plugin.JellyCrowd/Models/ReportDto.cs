namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Payload to report an issue about a title.
/// </summary>
public class ReportDto
{
  /// <summary>Gets or sets the media type (<c>movie</c> or <c>tv</c>).</summary>
  public string MediaType { get; set; } = string.Empty;

  /// <summary>Gets or sets the TMDB identifier.</summary>
  public int TmdbId { get; set; }

  /// <summary>Gets or sets the title.</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the issue description.</summary>
  public string Message { get; set; } = string.Empty;
}
