using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IPlaybackHistoryStore"/>. Bounded by a most-recent cap and a retention window so
/// the JSON document stays manageable for a private server's playback volume (the "log retention" rule).
/// </summary>
public sealed class JsonPlaybackHistoryStore : IPlaybackHistoryStore, IDisposable
{
  private const int SchemaVersion = 1;
  private const int MaxEntries = 50000;
  private static readonly TimeSpan Retention = TimeSpan.FromDays(365);
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<PlaybackRecord>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonPlaybackHistoryStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonPlaybackHistoryStore(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public async Task AddAsync(PlaybackRecord record, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(record);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      record.Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id;
      items.Add(record);
      Prune(items);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<PlaybackRecord>> GetAllAsync(CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.OrderByDescending(r => r.PlayedAtUtc).ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<PlaybackRecord>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Where(r => r.PlayedAtUtc >= sinceUtc).OrderByDescending(r => r.PlayedAtUtc).ToList();
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

  private static void Prune(List<PlaybackRecord> items)
  {
    var cutoff = DateTime.UtcNow - Retention;
    items.RemoveAll(r => r.PlayedAtUtc < cutoff);
    if (items.Count > MaxEntries)
    {
      var keep = items.OrderByDescending(r => r.PlayedAtUtc).Take(MaxEntries).ToList();
      items.Clear();
      items.AddRange(keep);
    }
  }

  private async Task<List<PlaybackRecord>> LoadAsync(CancellationToken cancellationToken)
  {
    if (_cache is not null)
    {
      return _cache;
    }

    _cache = await VersionedJsonFile.ReadAsync<PlaybackRecord>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);
    return _cache;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _cache ?? new List<PlaybackRecord>(), SerializerOptions, cancellationToken);
}
