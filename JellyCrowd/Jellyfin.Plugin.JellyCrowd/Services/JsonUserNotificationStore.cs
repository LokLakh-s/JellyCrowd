using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IUserNotificationStore"/>. Bounded per user (most-recent cap + retention)
/// so the JSON file stays small regardless of activity.
/// </summary>
public sealed class JsonUserNotificationStore : IUserNotificationStore, IDisposable
{
  private const int MaxPerUser = 50;
  private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<UserNotification>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonUserNotificationStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonUserNotificationStore(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public async Task<UserNotification> AddAsync(UserNotification notification, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(notification);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      notification.Id = notification.Id == Guid.Empty ? Guid.NewGuid() : notification.Id;
      notification.CreatedAt = notification.CreatedAt == default ? DateTime.UtcNow : notification.CreatedAt;
      items.Add(notification);
      Prune(items, notification.UserId);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return notification;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<UserNotification>> GetByUserAsync(Guid userId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<int> MarkReadAsync(Guid userId, Guid? id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var count = 0;
      foreach (var n in items)
      {
        if (n.UserId == userId && !n.Read && (id is null || n.Id == id))
        {
          n.Read = true;
          count++;
        }
      }

      if (count > 0)
      {
        await SaveAsync(cancellationToken).ConfigureAwait(false);
      }

      return count;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<int> ClearAsync(Guid userId, Guid? id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var removed = items.RemoveAll(n => n.UserId == userId && (id is null || n.Id == id));
      if (removed > 0)
      {
        await SaveAsync(cancellationToken).ConfigureAwait(false);
      }

      return removed;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public void Dispose()
  {
    _mutex.Dispose();
    GC.SuppressFinalize(this);
  }

  // Keep only the most recent MaxPerUser entries for the user, and drop anything past the retention.
  private static void Prune(List<UserNotification> items, Guid userId)
  {
    var cutoff = DateTime.UtcNow - Retention;
    items.RemoveAll(n => n.UserId == userId && n.CreatedAt < cutoff);

    var mine = items.Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).ToList();
    for (var i = MaxPerUser; i < mine.Count; i++)
    {
      items.Remove(mine[i]);
    }
  }

  private async Task<List<UserNotification>> LoadAsync(CancellationToken cancellationToken)
  {
    if (_cache is not null)
    {
      return _cache;
    }

    if (File.Exists(_filePath))
    {
      using var stream = File.OpenRead(_filePath);
      _cache = await JsonSerializer.DeserializeAsync<List<UserNotification>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
               ?? new List<UserNotification>();
    }
    else
    {
      _cache = new List<UserNotification>();
    }

    return _cache;
  }

  private async Task SaveAsync(CancellationToken cancellationToken)
  {
    var directory = Path.GetDirectoryName(_filePath);
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }

    var tempPath = _filePath + ".tmp";
    using (var stream = File.Create(tempPath))
    {
      await JsonSerializer.SerializeAsync(stream, _cache, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    File.Move(tempPath, _filePath, overwrite: true);
  }
}
