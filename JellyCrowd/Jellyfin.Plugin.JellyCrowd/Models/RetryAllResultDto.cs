namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The outcome of retrying every approved-but-not-yet-available request in one bulk action.
/// </summary>
public class RetryAllResultDto
{
  /// <summary>
  /// Gets or sets the number of requests whose retry was attempted successfully.
  /// </summary>
  public int Retried { get; set; }

  /// <summary>
  /// Gets or sets the number of requests whose retry failed.
  /// </summary>
  public int Failed { get; set; }

  /// <summary>
  /// Gets or sets the total number of requests considered (approved and not yet available).
  /// </summary>
  public int Total { get; set; }
}
