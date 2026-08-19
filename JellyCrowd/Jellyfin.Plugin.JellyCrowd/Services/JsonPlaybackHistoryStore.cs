using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IPlaybackHistoryStore"/>. This is also each user's personal viewing history,
/// which by design is never auto-erased — only the user clears their own — so there is no time-based
/// retention. A large most-recent cap remains purely as a safety bound against pathological growth of the
/// single JSON document; on a private server it is years of history and is never reached in practice.
/// </summary>
public sealed class JsonPlaybackHistoryStore : IPlaybackHistoryStore, IDisposable
{
  private const int SchemaVersion = 1;
  private const int MaxEntries = 200000;
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
  public async Task<int> AddRangeAsync(IEnumerable<PlaybackRecord> records, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(records);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var before = items.Count;
      foreach (var record in records)
      {
        record.Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id;
        items.Add(record);
      }

      Prune(items);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return Math.Max(0, items.Count - before);
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
  public async Task<IReadOnlyList<PlaybackRecord>> GetByUserAsync(Guid userId, int limit, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var mine = items.Where(r => r.UserId == userId).OrderByDescending(r => r.PlayedAtUtc);
      return (limit > 0 ? mine.Take(limit) : mine).ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<int> DeleteByUserAsync(Guid userId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var removed = items.RemoveAll(r => r.UserId == userId);
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
  public async Task<bool> DeleteOneAsync(Guid userId, Guid recordId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      // The user id must match too: a record id alone must never let one user delete another's entry.
      var removed = items.RemoveAll(r => r.Id == recordId && r.UserId == userId);
      if (removed > 0)
      {
        await SaveAsync(cancellationToken).ConfigureAwait(false);
      }

      return removed > 0;
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

  // Only a safety cap against unbounded growth of the JSON document — there is no time-based expiry, so a
  // user's history stays until they clear it themselves.
  private static void Prune(List<PlaybackRecord> items)
  {
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
