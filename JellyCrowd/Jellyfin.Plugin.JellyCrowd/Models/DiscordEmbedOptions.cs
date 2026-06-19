namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Per-notification presentation options for a Discord embed, resolved from the plugin configuration
/// for a given event. Keeps <see cref="Services.NotificationEmbeds"/> pure and testable.
/// </summary>
public sealed class DiscordEmbedOptions
{
  /// <summary>Gets or sets the embed accent color (integer RGB).</summary>
  public int Color { get; set; }

  /// <summary>Gets or sets a value indicating whether to show the poster thumbnail.</summary>
  public bool ShowPoster { get; set; } = true;

  /// <summary>Gets or sets a value indicating whether to use the synopsis as the description.</summary>
  public bool ShowSynopsis { get; set; } = true;

  /// <summary>Gets or sets a value indicating whether to show the "Requested by" field.</summary>
  public bool ShowRequestedBy { get; set; } = true;

  /// <summary>Gets or sets a value indicating whether to show the "Status" field.</summary>
  public bool ShowStatus { get; set; } = true;

  /// <summary>Gets or sets a value indicating whether to show the "Season" field.</summary>
  public bool ShowSeason { get; set; } = true;

  /// <summary>Gets or sets a value indicating whether the title links to the TMDB page.</summary>
  public bool ShowLink { get; set; } = true;

  /// <summary>Gets or sets an optional message posted above the embed (role/user ping).</summary>
  public string? Mention { get; set; }
}
