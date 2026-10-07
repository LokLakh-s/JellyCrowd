using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="ISeriesMappingStore"/>: one small JSON document, held in memory.
/// </summary>
public sealed class JsonSeriesMappingStore : ISeriesMappingStore, IDisposable
{
  private const int SchemaVersion = 1;
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private readonly ConcurrentDictionary<int, SeriesMapping> _byTmdb = new();
  private bool _loaded;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonSeriesMappingStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonSeriesMappingStore(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public SeriesMapping? Get(int tmdbId) => _byTmdb.TryGetValue(tmdbId, out var mapping) ? mapping : null;

  /// <inheritdoc />
  public IReadOnlyList<SeriesMapping> ForTvdb(int tvdbId) => _byTmdb.Values.Where(m => m.TvdbId == tvdbId).ToList();

  /// <inheritdoc />
  public async Task LoadAsync(CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task SetAsync(SeriesMapping mapping, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(mapping);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
      _byTmdb[mapping.TmdbId] = mapping;
      await SaveAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task RemoveAsync(int tmdbId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
      if (_byTmdb.TryRemove(tmdbId, out _))
      {
        await SaveAsync(cancellationToken).ConfigureAwait(false);
      }
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

  private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
  {
    if (_loaded)
    {
      return;
    }

    foreach (var mapping in await VersionedJsonFile.ReadAsync<SeriesMapping>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false))
    {
      _byTmdb[mapping.TmdbId] = mapping;
    }

    _loaded = true;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _byTmdb.Values.OrderBy(m => m.TmdbId).ToList(), SerializerOptions, cancellationToken);
}
