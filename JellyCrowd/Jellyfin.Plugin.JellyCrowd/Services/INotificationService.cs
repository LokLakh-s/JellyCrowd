using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Delivers request notifications to the configured channels (Discord webhook, email).
/// </summary>
public interface INotificationService
{
  /// <summary>
  /// Sends a notification for a request lifecycle event. Never throws; delivery failures are logged.
  /// </summary>
  /// <param name="request">The request the event concerns.</param>
  /// <param name="notificationEvent">The lifecycle event.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when delivery has been attempted.</returns>
  Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken);

  /// <summary>
  /// Sends a single grouped "now available" notification for several requests that became available at
  /// once — typically multiple episodes of the same season dropping together — so each channel gets one
  /// message instead of one per episode. A single-item batch behaves exactly like
  /// <see cref="NotifyRequestEventAsync"/> with <see cref="NotificationEvent.Available"/>. Never throws.
  /// </summary>
  /// <param name="requests">The requests that just became available (same recipient/title/season).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when delivery has been attempted.</returns>
  Task NotifyAvailableBatchAsync(System.Collections.Generic.IReadOnlyList<RequestRecord> requests, CancellationToken cancellationToken);

  /// <summary>
  /// Sends a standalone personal notification (in-app bell + the user's opted-in personal channels) that
  /// is not tied to a request lifecycle event, e.g. a quota or ownership-expiry warning. Never throws.
  /// </summary>
  /// <param name="userId">The recipient user id.</param>
  /// <param name="kind">The notification category (gates personal-channel delivery by opt-in).</param>
  /// <param name="title">The media/title the notice concerns (shown in the bell).</param>
  /// <param name="subject">The short subject line.</param>
  /// <param name="body">The message body.</param>
  /// <param name="posterPath">An optional poster path for the bell entry.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when delivery has been attempted.</returns>
  Task NotifyPersonalAsync(System.Guid userId, PersonalNotifyKind kind, string title, string subject, string body, string? posterPath, CancellationToken cancellationToken);

  /// <summary>
  /// Sends a test notification to a single channel (<c>discord</c> or <c>email</c>). Throws on failure
  /// or when the channel is not configured, so the caller can report the outcome.
  /// </summary>
  /// <param name="channel">The channel to test (<c>discord</c> or <c>email</c>).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the test message has been sent.</returns>
  Task SendTestAsync(string channel, CancellationToken cancellationToken);
}
