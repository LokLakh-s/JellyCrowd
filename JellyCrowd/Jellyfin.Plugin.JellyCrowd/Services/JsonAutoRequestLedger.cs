using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IAutoRequestLedger"/> (one small JSON document). Bounded: past
/// <see cref="MaxEntries"/> by default, the oldest entries are dropped.
/// </summary>
public sealed class JsonAutoRequestLedger : IAutoRequestLedger, IDisposable
{
  /// <summary>
  /// The most entries kept by default.
  /// </summary>
  public const int MaxEntries = 5000;

  private const int SchemaVersion = 1;
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly Func<DateTime> _now;
  private readonly int _maxEntries;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<AutoRequestEntry>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonAutoRequestLedger"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the ledger.</param>
  /// <param name="now">Clock accessor (defaults to <see cref="DateTime.UtcNow"/>); injectable for tests.</param>
  /// <param name="maxEntries">The most entries kept (defaults to <see cref="MaxEntries"/>).</param>
  public JsonAutoRequestLedger(string filePath, Func<DateTime>? now = null, int maxEntries = MaxEntries)
  {
    _filePath = filePath;
    _now = now ?? (() => DateTime.UtcNow);
    _maxEntries = Math.Max(1, maxEntries);
  }

  /// <inheritdoc />
  public async Task<bool> ContainsAsync(Guid userId, int tmdbId, int season, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Any(e => e.UserId == userId && e.TmdbId == tmdbId && e.Season == season);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task RecordAsync(Guid userId, int tmdbId, int season, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      if (items.Any(e => e.UserId == userId && e.TmdbId == tmdbId && e.Season == season))
      {
        return;
      }

      items.Add(new AutoRequestEntry { UserId = userId, TmdbId = tmdbId, Season = season, AtUtc = _now() });
      if (items.Count > _maxEntries)
      {
        items.RemoveRange(0, items.Count - _maxEntries);
      }

      await VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, items, SerializerOptions, cancellationToken).ConfigureAwait(false);
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

  private async Task<List<AutoRequestEntry>> LoadAsync(CancellationToken cancellationToken)
  {
    _cache ??= await VersionedJsonFile.ReadAsync<AutoRequestEntry>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);
    return _cache;
  }
}
