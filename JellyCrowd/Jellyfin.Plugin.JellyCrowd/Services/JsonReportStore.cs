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
/// File-backed <see cref="IReportStore"/>. Globally bounded (most-recent cap) so the store stays small.
/// </summary>
public sealed class JsonReportStore : IReportStore, IDisposable
{
  private const int MaxReports = 500;
  private const int SchemaVersion = 1;
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<MediaReport>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonReportStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonReportStore(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public async Task<MediaReport> AddAsync(MediaReport report, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(report);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      report.Id = report.Id == Guid.Empty ? Guid.NewGuid() : report.Id;
      report.CreatedAt = report.CreatedAt == default ? DateTime.UtcNow : report.CreatedAt;
      items.Add(report);

      // Keep only the most recent MaxReports.
      if (items.Count > MaxReports)
      {
        var keep = items.OrderByDescending(r => r.CreatedAt).Take(MaxReports).ToList();
        items.Clear();
        items.AddRange(keep);
      }

      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return report;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<MediaReport>> GetAllAsync(CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.OrderByDescending(r => r.CreatedAt).ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<MediaReport?> SetResolvedAsync(Guid id, bool resolved, string? response, CancellationToken cancellationToken)
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

      record.Resolved = resolved;
      if (!string.IsNullOrWhiteSpace(response))
      {
        record.AdminResponse = response.Trim();
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
  public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var removed = items.RemoveAll(r => r.Id == id) > 0;
      if (removed)
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

  private async Task<List<MediaReport>> LoadAsync(CancellationToken cancellationToken)
  {
    if (_cache is not null)
    {
      return _cache;
    }

    _cache = await VersionedJsonFile.ReadAsync<MediaReport>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);
    return _cache;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _cache ?? new List<MediaReport>(), SerializerOptions, cancellationToken);
}
