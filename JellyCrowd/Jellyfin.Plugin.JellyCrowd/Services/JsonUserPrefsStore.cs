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
/// File-backed <see cref="IUserPrefsStore"/> (one small JSON document, one entry per user).
/// </summary>
public sealed class JsonUserPrefsStore : IUserPrefsStore, IDisposable
{
  private const int SchemaVersion = 1;
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<UserNotificationPrefs>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonUserPrefsStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonUserPrefsStore(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public async Task<UserNotificationPrefs> GetAsync(Guid userId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.FirstOrDefault(p => p.UserId == userId) ?? new UserNotificationPrefs { UserId = userId };
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<UserNotificationPrefs> SetAsync(UserNotificationPrefs prefs, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(prefs);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      items.RemoveAll(p => p.UserId == prefs.UserId);
      items.Add(prefs);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return prefs;
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

  private async Task<List<UserNotificationPrefs>> LoadAsync(CancellationToken cancellationToken)
  {
    if (_cache is not null)
    {
      return _cache;
    }

    _cache = await VersionedJsonFile.ReadAsync<UserNotificationPrefs>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);
    return _cache;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _cache ?? new List<UserNotificationPrefs>(), SerializerOptions, cancellationToken);
}
