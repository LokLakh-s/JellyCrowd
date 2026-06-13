namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Payload passed by the File Transformation plugin to a transformation callback.
/// </summary>
public class TransformationRequest
{
  /// <summary>
  /// Gets or sets the current contents of the file being transformed.
  /// </summary>
  public string Contents { get; set; } = string.Empty;
}
