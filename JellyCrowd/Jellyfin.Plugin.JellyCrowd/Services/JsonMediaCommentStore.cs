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
/// File-backed <see cref="IMediaCommentStore"/>. Bounded per title (most-recent cap) so the JSON
/// document stays small regardless of activity.
/// </summary>
public sealed class JsonMediaCommentStore : IMediaCommentStore, IDisposable
{
  private const int MaxPerTitle = 200;
  private const int SchemaVersion = 1;
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<MediaComment>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonMediaCommentStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonMediaCommentStore(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public async Task<MediaComment> AddAsync(MediaComment comment, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(comment);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      comment.Id = comment.Id == Guid.Empty ? Guid.NewGuid() : comment.Id;
      comment.CreatedAt = comment.CreatedAt == default ? DateTime.UtcNow : comment.CreatedAt;
      items.Add(comment);
      TrimTitle(items, comment.MediaType, comment.TmdbId);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return comment;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<MediaComment> AddOrUpdateAsync(MediaComment review, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(review);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var existing = items.FirstOrDefault(c =>
        c.UserId == review.UserId
        && c.TmdbId == review.TmdbId
        && string.Equals(c.MediaType, review.MediaType, StringComparison.Ordinal));

      if (existing is not null)
      {
        existing.Text = review.Text;
        existing.Rating = review.Rating;
        existing.UserName = review.UserName;
        existing.CreatedAt = DateTime.UtcNow;
        existing.Hidden = false; // a fresh edit un-hides; admin can re-hide
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return existing;
      }

      review.Id = review.Id == Guid.Empty ? Guid.NewGuid() : review.Id;
      review.CreatedAt = DateTime.UtcNow;
      items.Add(review);
      TrimTitle(items, review.MediaType, review.TmdbId);
      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return review;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<MediaComment>> GetForTitleAsync(string mediaType, int tmdbId, bool includeHidden, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items
        .Where(c => c.TmdbId == tmdbId
          && string.Equals(c.MediaType, mediaType, StringComparison.Ordinal)
          && (includeHidden || !c.Hidden))
        .OrderByDescending(c => c.CreatedAt)
        .ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<MediaComment?> SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var record = items.FirstOrDefault(c => c.Id == id);
      if (record is null)
      {
        return null;
      }

      record.Hidden = hidden;
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
      var removed = items.RemoveAll(c => c.Id == id) > 0;
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
  public async Task<MediaComment?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.FirstOrDefault(c => c.Id == id);
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

  private static void TrimTitle(List<MediaComment> items, string mediaType, int tmdbId)
  {
    var forTitle = items
      .Where(c => c.TmdbId == tmdbId && string.Equals(c.MediaType, mediaType, StringComparison.Ordinal))
      .OrderByDescending(c => c.CreatedAt)
      .ToList();
    for (var i = MaxPerTitle; i < forTitle.Count; i++)
    {
      items.Remove(forTitle[i]);
    }
  }

  private async Task<List<MediaComment>> LoadAsync(CancellationToken cancellationToken)
  {
    if (_cache is not null)
    {
      return _cache;
    }

    _cache = await VersionedJsonFile.ReadAsync<MediaComment>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);
    return _cache;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _cache ?? new List<MediaComment>(), SerializerOptions, cancellationToken);
}
