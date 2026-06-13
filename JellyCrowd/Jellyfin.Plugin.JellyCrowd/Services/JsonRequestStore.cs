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
/// File-backed <see cref="IRequestStore"/> persisting requests as JSON in the plugin data folder.
/// Requests are low-volume, so a JSON document (cached in memory, written atomically) is sufficient
/// and avoids a native SQLite dependency.
/// </summary>
public sealed class JsonRequestStore : IRequestStore, IDisposable
{
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<RequestRecord>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonRequestStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonRequestStore(string filePath)
  {
    _filePath = filePath;
  }

  /// <inheritdoc />
  public async Task<RequestRecord> CreateAsync(RequestRecord record, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(record);

    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      record.Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id;
      record.RequestedAt = DateTime.UtcNow;
      items.Add(record);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<RequestRecord>> GetAllAsync(CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.OrderByDescending(r => r.RequestedAt).ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<RequestRecord>> GetByUserAsync(Guid userId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Where(r => r.UserId == userId).OrderByDescending(r => r.RequestedAt).ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.FirstOrDefault(r => r.Id == id);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> UpdateStatusAsync(Guid id, RequestStatus status, Guid decidedBy, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);
      if (record is null)
      {
        return null;
      }

      record.Status = status;
      record.DecidedAt = DateTime.UtcNow;
      record.DecidedBy = decidedBy;
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> ExistsActiveAsync(Guid userId, int tmdbId, string mediaType, int? season, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Any(r =>
        r.UserId == userId
        && r.TmdbId == tmdbId
        && string.Equals(r.MediaType, mediaType, StringComparison.Ordinal)
        && r.Season == season
        && r.Status != RequestStatus.Denied);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<int> CountUserRequestsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Count(r => r.UserId == userId && r.Status != RequestStatus.Denied && r.RequestedAt >= sinceUtc);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> MarkAvailableAsync(Guid id, string jellyfinItemId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);
      if (record is null)
      {
        return null;
      }

      record.Status = RequestStatus.Available;
      record.JellyfinItemId = jellyfinItemId;
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.UserId != userId || record.Status != RequestStatus.Pending)
      {
        return false;
      }

      items.Remove(record);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return true;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> RequestDeletionAsync(Guid id, Guid userId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.UserId != userId || record.Status != RequestStatus.Available || record.DeletionRequestedAt is not null)
      {
        return null;
      }

      record.DeletionRequestedAt = DateTime.UtcNow;
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> AnyActiveReferenceAsync(Guid excludeId, int tmdbId, string mediaType, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Any(r =>
        r.Id != excludeId
        && r.TmdbId == tmdbId
        && string.Equals(r.MediaType, mediaType, StringComparison.Ordinal)
        && r.DeletionRequestedAt is null
        && r.Status != RequestStatus.Denied);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<RequestRecord>> GetDueForDeletionAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Where(r => r.DeletionRequestedAt is not null && r.DeletionRequestedAt <= cutoffUtc).ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      items.RemoveAll(r => r.Id == id);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
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

  private async Task<List<RequestRecord>> LoadAsync(CancellationToken cancellationToken)
  {
    if (_cache is not null)
    {
      return _cache;
    }

    if (File.Exists(_filePath))
    {
      using var stream = File.OpenRead(_filePath);
      _cache = await JsonSerializer.DeserializeAsync<List<RequestRecord>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
               ?? new List<RequestRecord>();
    }
    else
    {
      _cache = new List<RequestRecord>();
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
