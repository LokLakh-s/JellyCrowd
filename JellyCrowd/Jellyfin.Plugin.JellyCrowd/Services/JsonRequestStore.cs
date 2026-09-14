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
  private const int SchemaVersion = 1;

  // Ceiling on stored requests, so the file cannot grow without bound (a user can create one request per
  // distinct title, faster than they are ever completed). Only DENIED requests are pruned, oldest first:
  // they are dead history, whereas Pending/Approved are live work and Available IS the ownership record
  // the quota is computed from — dropping either would lose real state, so we never do.
  private const int MaxStoredRequests = 5000;

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
      Trim(items);
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
      record.HeldForQuota = false; // an explicit decision supersedes the quota hold
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> PromoteFromQuotaHoldAsync(Guid id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.Status != RequestStatus.Pending || !record.HeldForQuota)
      {
        return null;
      }

      record.Status = RequestStatus.Approved;
      record.HeldForQuota = false;
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> HoldForQuotaAsync(Guid id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);

      // Only an approved, not-yet-dispatched request can be put back on a quota hold — never touch one
      // already dispatched/available/denied (nor re-hold a pending one).
      if (record is null || record.Status != RequestStatus.Approved || record.DispatchedAt is not null)
      {
        return null;
      }

      record.Status = RequestStatus.Pending;
      record.HeldForQuota = true;
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
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
      record.AvailableAt = DateTime.UtcNow;
      record.HeldForQuota = false; // fulfilled — no longer a quota hold
      record.DispatchError = null; // the title is here now — any earlier dispatch failure is moot
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> RenewAvailableAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken)
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

      record.AvailableAt = whenUtc;
      record.DeletionRequestedAt = null; // renewing keeps the media
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<RequestRecord>> ExpireOwnershipsAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var lapsed = items.Where(r =>
        r.Status == RequestStatus.Available
        && r.DeletionRequestedAt is null
        && r.AvailableAt is { } at
        && at < cutoffUtc).ToList();
      if (lapsed.Count > 0)
      {
        items.RemoveAll(r => lapsed.Contains(r));
        await SaveAsync(cancellationToken).ConfigureAwait(false);
      }

      return lapsed;
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
      if (record is null
          || record.UserId != userId
          || (record.Status != RequestStatus.Pending && record.Status != RequestStatus.Approved))
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
  public async Task<RequestRecord?> AdminFlagDeletionAsync(Guid id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.Status != RequestStatus.Available || record.DeletionRequestedAt is not null)
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
  public async Task<RequestRecord?> CancelDeletionAsync(Guid id, Guid userId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.UserId != userId || record.DeletionRequestedAt is null)
      {
        return null;
      }

      record.DeletionRequestedAt = null;
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> AnyActiveReferenceAsync(Guid excludeId, int tmdbId, string mediaType, int? season, int? episode, CancellationToken cancellationToken)
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
        && r.Status != RequestStatus.Denied
        && MediaScope.Overlaps(season, episode, r.Season, r.Episode));
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
  public async Task<RequestRecord?> AdminUpdateAsync(Guid id, RequestStatus status, int? season, int? episode, DateTime? desiredAt, CancellationToken cancellationToken)
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
      record.Season = season;
      record.Episode = episode;
      record.DesiredAt = desiredAt;
      record.HeldForQuota = false; // an explicit admin edit supersedes the quota hold
      // Stamp the ownership start when an admin flips a request to Available (as MarkAvailableAsync does),
      // so the expiry countdown (My media) and the ownership-expiry task have a reference point.
      if (status == RequestStatus.Available && record.AvailableAt is null)
      {
        record.AvailableAt = DateTime.UtcNow;
      }

      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> MarkDispatchedAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken)
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

      record.DispatchedAt = whenUtc;
      record.DispatchAttemptedAt = whenUtc;
      record.DispatchError = null; // success clears any previous failure
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> SetDispatchErrorAsync(Guid id, string? error, DateTime whenUtc, CancellationToken cancellationToken)
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

      record.DispatchError = error;
      record.DispatchAttemptedAt = whenUtc;
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> SetJellyfinItemIdAsync(Guid id, string jellyfinItemId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);
      if (record is null || string.Equals(record.JellyfinItemId, jellyfinItemId, StringComparison.OrdinalIgnoreCase))
      {
        return null;
      }

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
  public async Task<RequestRecord?> MarkNotFoundNotifiedAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.NotFoundNotifiedAt is not null)
      {
        return null;
      }

      record.NotFoundNotifiedAt = whenUtc;
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RequestRecord?> RecordProgressAsync(Guid id, int presentEpisodes, DateTime whenUtc, bool restartOwnershipClock, CancellationToken cancellationToken)
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

      record.PresentEpisodes = presentEpisodes;
      record.ProgressAt = whenUtc;

      // Never undo a user's deletion request: a new episode arriving must not silently keep flagged media.
      if (restartOwnershipClock && record.Status == RequestStatus.Available && record.DeletionRequestedAt is null)
      {
        record.AvailableAt = whenUtc;
      }

      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return record;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<RequestRecord>> GetDueForDispatchAsync(DateTime nowUtc, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.Where(r =>
        r.Status == RequestStatus.Approved
        && r.DispatchedAt is null
        && (r.DesiredAt is null || r.DesiredAt <= nowUtc)).ToList();
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

    _cache = await VersionedJsonFile.ReadAsync<RequestRecord>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);

    // One-time backfill: records made Available via an older admin status edit never got an AvailableAt
    // stamp, so their expiry countdown and ownership-expiry had no reference point (no expiry shown, and
    // they never lapsed). Stamp them now — a fresh window from here, to avoid a surprise lapse on upgrade.
    var now = DateTime.UtcNow;
    var backfilled = false;
    foreach (var record in _cache)
    {
      if (record.Status == RequestStatus.Available && record.AvailableAt is null)
      {
        record.AvailableAt = now;
        backfilled = true;
      }
    }

    if (backfilled)
    {
      await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    return _cache;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _cache ?? new List<RequestRecord>(), SerializerOptions, cancellationToken);

  /// <summary>
  /// Drops the oldest denied requests once the store is over its ceiling. Nothing else is ever discarded:
  /// if only live/owned requests remain, the store is allowed to exceed the ceiling rather than lose real
  /// state. Internal so the bound can be unit-tested without writing thousands of files.
  /// </summary>
  /// <param name="items">The records, trimmed in place.</param>
  internal static void Trim(List<RequestRecord> items)
  {
    var excess = items.Count - MaxStoredRequests;
    if (excess <= 0)
    {
      return;
    }

    var prunable = items
      .Where(r => r.Status == RequestStatus.Denied)
      .OrderBy(r => r.RequestedAt)
      .Take(excess)
      .ToList();

    foreach (var record in prunable)
    {
      items.Remove(record);
    }
  }
}
