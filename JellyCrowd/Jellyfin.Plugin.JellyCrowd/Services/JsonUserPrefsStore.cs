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

    if (File.Exists(_filePath))
    {
      using var stream = File.OpenRead(_filePath);
      _cache = await JsonSerializer.DeserializeAsync<List<UserNotificationPrefs>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
               ?? new List<UserNotificationPrefs>();
    }
    else
    {
      _cache = new List<UserNotificationPrefs>();
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
