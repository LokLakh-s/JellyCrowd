using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Stores each user's personal notification-delivery preferences.
/// </summary>
public interface IUserPrefsStore
{
  /// <summary>
  /// Gets a user's preferences, or sensible defaults (enabled, no channels) when none are saved.
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The preferences.</returns>
  Task<UserNotificationPrefs> GetAsync(Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Saves (upserts) a user's preferences.
  /// </summary>
  /// <param name="prefs">The preferences to save.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The saved preferences.</returns>
  Task<UserNotificationPrefs> SetAsync(UserNotificationPrefs prefs, CancellationToken cancellationToken);
}
