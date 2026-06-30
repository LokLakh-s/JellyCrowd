namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The result of a branding image upload: the relative URL the admin's branding field should store.
/// </summary>
public class BrandingUploadResult
{
  /// <summary>
  /// Gets or sets the relative URL of the stored image (e.g. <c>JellyCrowd/Branding/Image/&lt;name&gt;</c>).
  /// </summary>
  public string Url { get; set; } = string.Empty;
}
