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
/// File-backed <see cref="IWatchlistStore"/> persisting watchlist entries as JSON in the plugin data folder.
/// </summary>
public sealed class JsonWatchlistStore : IWatchlistStore, IDisposable
{
  private const int SchemaVersion = 1;
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<WatchlistEntry>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonWatchlistStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonWatchlistStore(string filePath)
  {
    _filePath = filePath;
  }

  /// <inheritdoc />
  public async Task<WatchlistEntry> AddAsync(WatchlistEntry entry, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(entry);

    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var existing = items.FirstOrDefault(e =>
        e.UserId == entry.UserId
        && e.TmdbId == entry.TmdbId
        && string.Equals(e.MediaType, entry.MediaType, StringComparison.Ordinal));
      if (existing is not null)
      {
        return existing;
      }

      entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
      entry.AddedAt = DateTime.UtcNow;
      items.Add(entry);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return entry;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> RemoveAsync(Guid userId, int tmdbId, string mediaType, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var removed = items.RemoveAll(e =>
        e.UserId == userId
        && e.TmdbId == tmdbId
        && string.Equals(e.MediaType, mediaType, StringComparison.Ordinal));
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
  public async Task<IReadOnlyList<WatchlistEntry>> GetByUserAsync(Guid userId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Where(e => e.UserId == userId).OrderByDescending(e => e.AddedAt).ToList();
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

  private async Task<List<WatchlistEntry>> LoadAsync(CancellationToken cancellationToken)
  {
    if (_cache is not null)
    {
      return _cache;
    }

    _cache = await VersionedJsonFile.ReadAsync<WatchlistEntry>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);
    return _cache;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _cache ?? new List<WatchlistEntry>(), SerializerOptions, cancellationToken);
}
