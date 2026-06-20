namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The outcome of a single diagnostics/health check shown in the admin Diagnostics tab.
/// </summary>
public class DiagnosticResult
{
  /// <summary>Gets or sets the check name.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>Gets or sets the status: <c>ok</c>, <c>warning</c>, <c>error</c> or <c>info</c>.</summary>
  public string Status { get; set; } = "ok";

  /// <summary>Gets or sets the human-readable detail / resolution hint.</summary>
  public string Detail { get; set; } = string.Empty;
}
