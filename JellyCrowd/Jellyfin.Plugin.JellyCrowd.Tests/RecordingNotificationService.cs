using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;

namespace Jellyfin.Plugin.JellyCrowd.Tests;

/// <summary>
/// An <see cref="INotificationService"/> double that records the events it was asked to send.
/// </summary>
internal sealed class RecordingNotificationService : INotificationService
{
  public List<NotificationEvent> Events { get; } = new();

  public Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken)
  {
    Events.Add(notificationEvent);
    return Task.CompletedTask;
  }

  public Task SendTestAsync(string channel, CancellationToken cancellationToken) => Task.CompletedTask;
}
