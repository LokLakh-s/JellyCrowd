using System;
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

  public List<(Guid UserId, PersonalNotifyKind Kind, string Title)> Personal { get; } = new();

  // Each entry is one batched "now available" call and the number of requests it grouped.
  public List<int> AvailableBatches { get; } = new();

  public Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken)
  {
    Events.Add(notificationEvent);
    return Task.CompletedTask;
  }

  public Task NotifyAvailableBatchAsync(IReadOnlyList<RequestRecord> requests, CancellationToken cancellationToken)
  {
    AvailableBatches.Add(requests.Count);
    Events.Add(NotificationEvent.Available);
    return Task.CompletedTask;
  }

  public Task NotifyPersonalAsync(Guid userId, PersonalNotifyKind kind, string title, string subject, string body, string? posterPath, CancellationToken cancellationToken)
  {
    Personal.Add((userId, kind, title));
    return Task.CompletedTask;
  }

  // Each entry is one administrator broadcast (a report coming in, or a reminder about the open ones).
  public List<(string Subject, string Body)> AdminNotices { get; } = new();

  public Task NotifyAdminsAsync(string subject, string body, CancellationToken cancellationToken)
  {
    AdminNotices.Add((subject, body));
    return Task.CompletedTask;
  }

  public Task SendTestAsync(string channel, CancellationToken cancellationToken) => Task.CompletedTask;


  public Task SendPersonalTestAsync(System.Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
}
