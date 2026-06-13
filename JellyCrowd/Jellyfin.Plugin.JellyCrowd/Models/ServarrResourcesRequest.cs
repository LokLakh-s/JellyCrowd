namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Admin request to fetch a Radarr/Sonarr instance's selectable resources, using credentials typed
/// in the configuration form (before they are saved).
/// </summary>
public sealed class ServarrResourcesRequest
{
  /// <summary>
  /// Gets or sets the service, either <c>radarr</c> or <c>sonarr</c> (controls language-profile fetch).
  /// </summary>
  public string Service { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the instance base URL.
  /// </summary>
  public string Url { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the instance API key.
  /// </summary>
  public string ApiKey { get; set; } = string.Empty;
}
