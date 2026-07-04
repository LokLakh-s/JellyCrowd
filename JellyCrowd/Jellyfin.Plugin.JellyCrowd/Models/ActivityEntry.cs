using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One entry in the plugin's internal activity log (admin Logs tab).
/// </summary>
public class ActivityEntry
{
  /// <summary>Gets or sets the unique entry id.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the UTC time of the event.</summary>
  public DateTime Timestamp { get; set; }

  /// <summary>Gets or sets the severity: <c>info</c>, <c>warning</c> or <c>error</c>.</summary>
  public string Level { get; set; } = "info";

  /// <summary>Gets or sets the category: <c>request</c>, <c>download</c>, <c>admin</c>, <c>user</c>, <c>system</c>.</summary>
  public string Category { get; set; } = "system";

  /// <summary>
  /// Gets or sets the display name of the user this entry concerns (the actor or subject), or
  /// <c>null</c> for entries with no specific user (system / config / backend events). Used by the
  /// admin Logs tab to filter activity by user.
  /// </summary>
  public string? User { get; set; }

  /// <summary>Gets or sets the human-readable message.</summary>
  public string Message { get; set; } = string.Empty;
}
