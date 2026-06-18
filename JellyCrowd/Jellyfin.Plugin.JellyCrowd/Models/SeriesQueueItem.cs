namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A Sonarr queue record reduced to what is needed to match it back to a request: the series TVDB
/// id, the episode's season/number, and its download progress.
/// </summary>
public class SeriesQueueItem
{
  /// <summary>
  /// Gets or sets the TVDB id of the series this download belongs to.
  /// </summary>
  public int TvdbId { get; set; }

  /// <summary>
  /// Gets or sets the season number of the downloading episode, if known.
  /// </summary>
  public int? Season { get; set; }

  /// <summary>
  /// Gets or sets the episode number of the downloading episode, if known.
  /// </summary>
  public int? Episode { get; set; }

  /// <summary>
  /// Gets or sets the download progress.
  /// </summary>
  public QueueProgress Progress { get; set; } = new QueueProgress();
}
