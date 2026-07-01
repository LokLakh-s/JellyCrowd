namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Payload to resolve a report, with an optional note delivered to the reporter.
/// </summary>
public class ResolveReportDto
{
  /// <summary>Gets or sets an optional admin note shown to the reporter.</summary>
  public string? Response { get; set; }
}
