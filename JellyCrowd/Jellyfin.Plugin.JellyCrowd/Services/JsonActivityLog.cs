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
/// File-backed <see cref="IActivityLog"/>. Bounded by a most-recent cap and a retention window so the
/// JSON document stays small (the "log rotation / retention" rule).
/// </summary>
public sealed class JsonActivityLog : IActivityLog, IDisposable
{
  private const int SchemaVersion = 1;
  private const int MaxEntries = 2000;
  private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<ActivityEntry>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonActivityLog"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the log.</param>
  public JsonActivityLog(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public Task LogAsync(string level, string category, string message, CancellationToken cancellationToken)
    => LogAsync(level, category, message, null, cancellationToken);

  /// <inheritdoc />
  public async Task LogAsync(string level, string category, string message, string? user, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      items.Add(new ActivityEntry
      {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        Level = string.IsNullOrWhiteSpace(level) ? "info" : level,
        Category = string.IsNullOrWhiteSpace(category) ? "system" : category,
        User = string.IsNullOrWhiteSpace(user) ? null : user,
        Message = message ?? string.Empty
      });
      Prune(items);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<ActivityEntry>> QueryAsync(string? term, string? category, string? level, string? user, int limit, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      IEnumerable<ActivityEntry> query = items;
      if (!string.IsNullOrWhiteSpace(category))
      {
        query = query.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));
      }

      if (!string.IsNullOrWhiteSpace(level))
      {
        query = query.Where(e => string.Equals(e.Level, level, StringComparison.OrdinalIgnoreCase));
      }

      if (!string.IsNullOrWhiteSpace(user))
      {
        query = query.Where(e => string.Equals(e.User, user, StringComparison.OrdinalIgnoreCase));
      }

      if (!string.IsNullOrWhiteSpace(term))
      {
        query = query.Where(e => e.Message.Contains(term, StringComparison.OrdinalIgnoreCase));
      }

      return query.OrderByDescending(e => e.Timestamp).Take(limit <= 0 ? 200 : limit).ToList();
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

  private static void Prune(List<ActivityEntry> items)
  {
    var cutoff = DateTime.UtcNow - Retention;
    items.RemoveAll(e => e.Timestamp < cutoff);
    if (items.Count > MaxEntries)
    {
      var keep = items.OrderByDescending(e => e.Timestamp).Take(MaxEntries).ToList();
      items.Clear();
      items.AddRange(keep);
    }
  }

  private async Task<List<ActivityEntry>> LoadAsync(CancellationToken cancellationToken)
  {
    if (_cache is not null)
    {
      return _cache;
    }

    _cache = await VersionedJsonFile.ReadAsync<ActivityEntry>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);
    return _cache;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _cache ?? new List<ActivityEntry>(), SerializerOptions, cancellationToken);
}
