using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Per-user in-app notification feed. Bounded: each user keeps only the most recent entries, and
/// entries older than the retention window are pruned, so the store never grows without limit.
/// </summary>
public interface IUserNotificationStore
{
  /// <summary>
  /// Adds a notification (assigning id/timestamp) and prunes the user's feed to the cap and retention.
  /// </summary>
  /// <param name="notification">The notification to add.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The stored notification.</returns>
  Task<UserNotification> AddAsync(UserNotification notification, CancellationToken cancellationToken);

  /// <summary>
  /// Gets a user's notifications, newest first.
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The notifications.</returns>
  Task<IReadOnlyList<UserNotification>> GetByUserAsync(Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Marks the user's notifications as read (a single one when <paramref name="id"/> is given, else all).
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="id">The notification id, or <c>null</c> for all.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The number of notifications updated.</returns>
  Task<int> MarkReadAsync(Guid userId, Guid? id, CancellationToken cancellationToken);

  /// <summary>
  /// Deletes the user's notifications (a single one when <paramref name="id"/> is given, else all).
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <param name="id">The notification id, or <c>null</c> for all.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The number of notifications removed.</returns>
  Task<int> ClearAsync(Guid userId, Guid? id, CancellationToken cancellationToken);
}
