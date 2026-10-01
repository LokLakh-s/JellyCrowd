using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IHiddenResumeStore"/> (one small JSON document). Bounded per user: past
/// <see cref="MaxPerUser"/>, a user's oldest entries are dropped.
/// </summary>
public sealed class JsonHiddenResumeStore : IHiddenResumeStore, IDisposable
{
  /// <summary>
  /// The most entries kept per user.
  /// </summary>
  public const int MaxPerUser = 200;

  private const int SchemaVersion = 1;
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<HiddenResumeEntry>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonHiddenResumeStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonHiddenResumeStore(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public async Task<IReadOnlyList<HiddenResumeEntry>> GetByUserAsync(Guid userId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Where(e => e.UserId == userId).OrderByDescending(e => e.HiddenAtUtc).ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task HideAsync(HiddenResumeEntry entry, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(entry);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      items.RemoveAll(e => e.UserId == entry.UserId && SameTarget(e, entry));
      items.Add(entry);
      var mine = items.Where(e => e.UserId == entry.UserId).OrderBy(e => e.HiddenAtUtc).ToList();
      foreach (var stale in mine.Take(Math.Max(0, mine.Count - MaxPerUser)))
      {
        items.Remove(stale);
      }

      await SaveAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> UnhideAsync(Guid userId, Guid itemId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var removed = items.RemoveAll(e => e.UserId == userId && (e.ItemId == itemId || e.SeriesId == itemId));
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
  public async Task<int> UnhideForPlaybackAsync(Guid userId, Guid itemId, Guid? seriesId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var removed = items.RemoveAll(e => e.UserId == userId
        && (e.ItemId == itemId || (seriesId.HasValue && e.SeriesId == seriesId)));
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

  // A movie is identified by itself; an episode by its show (removing one episode removes the show).
  private static bool SameTarget(HiddenResumeEntry a, HiddenResumeEntry b)
    => a.SeriesId.HasValue || b.SeriesId.HasValue ? a.SeriesId == b.SeriesId : a.ItemId == b.ItemId;

  private async Task<List<HiddenResumeEntry>> LoadAsync(CancellationToken cancellationToken)
  {
    _cache ??= await VersionedJsonFile.ReadAsync<HiddenResumeEntry>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);
    return _cache;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _cache ?? new List<HiddenResumeEntry>(), SerializerOptions, cancellationToken);
}
