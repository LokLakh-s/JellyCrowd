using System;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure resolution of a user's personal notification delivery targets from their preferences and the
/// admin-configured servers (SMTP / ntfy). Network-free so it can be unit tested.
/// </summary>
public static class PersonalDelivery
{
  private const string DefaultNtfyServer = "https://ntfy.sh";

  /// <summary>
  /// Whether to send the user a personal email: delivery enabled, an email is set, and SMTP is configured.
  /// </summary>
  /// <param name="prefs">The user's preferences.</param>
  /// <param name="config">The plugin configuration.</param>
  /// <returns><c>true</c> when an email should be sent.</returns>
  public static bool ShouldEmail(UserNotificationPrefs prefs, PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(prefs);
    ArgumentNullException.ThrowIfNull(config);
    return prefs.Enabled
      && !string.IsNullOrWhiteSpace(prefs.Email)
      && !string.IsNullOrWhiteSpace(config.SmtpHost)
      && !string.IsNullOrWhiteSpace(config.SmtpFromAddress);
  }

  /// <summary>
  /// The full ntfy publish URL for the user's topic, or <c>null</c> when ntfy delivery does not apply.
  /// </summary>
  /// <param name="prefs">The user's preferences.</param>
  /// <param name="config">The plugin configuration.</param>
  /// <returns>The ntfy URL, or <c>null</c>.</returns>
  public static string? NtfyUrl(UserNotificationPrefs prefs, PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(prefs);
    ArgumentNullException.ThrowIfNull(config);
    if (!prefs.Enabled || string.IsNullOrWhiteSpace(prefs.NtfyTopic))
    {
      return null;
    }

    var server = string.IsNullOrWhiteSpace(config.NtfyServer) ? DefaultNtfyServer : config.NtfyServer.TrimEnd('/');
    return server + "/" + prefs.NtfyTopic.Trim();
  }
}
