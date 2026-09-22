using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IPollStore"/>. Globally bounded (most-recent cap) so the store stays small;
/// closed polls are dropped before open ones, so a server that never cleans up cannot lose a live poll.
/// </summary>
public sealed class JsonPollStore : IPollStore, IDisposable
{
  private const int MaxPolls = 100;
  private const int SchemaVersion = 1;
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private List<Poll>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonPollStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonPollStore(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public async Task<IReadOnlyList<Poll>> GetAllAsync(CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.OrderByDescending(p => p.CreatedAt).ToList();
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<Poll?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      return items.FirstOrDefault(p => p.Id == id);
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<Poll> AddAsync(Poll poll, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(poll);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      poll.Id = poll.Id == Guid.Empty ? Guid.NewGuid() : poll.Id;
      poll.CreatedAt = poll.CreatedAt == default ? DateTime.UtcNow : poll.CreatedAt;
      items.Add(poll);

      // Over the cap, history goes first: closed polls (oldest first), and only then the open ones.
      if (items.Count > MaxPolls)
      {
        var keep = items
          .OrderBy(p => p.Closed ? 1 : 0)
          .ThenByDescending(p => p.CreatedAt)
          .Take(MaxPolls)
          .ToList();
        items.Clear();
        items.AddRange(keep);
      }

      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return poll;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<Poll?> UpdateAsync(Guid id, Poll updated, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(updated);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var poll = items.FirstOrDefault(p => p.Id == id);
      if (poll is null || poll.Votes.Count > 0)
      {
        return poll;
      }

      poll.Question = updated.Question;
      poll.Options = updated.Options;
      poll.MultiChoice = updated.MultiChoice;
      poll.ShowResults = updated.ShowResults;
      poll.ClosesAt = updated.ClosesAt;
      poll.GroupIds = updated.GroupIds;

      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return poll;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<Poll?> SetClosedAsync(Guid id, bool closed, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var poll = items.FirstOrDefault(p => p.Id == id);
      if (poll is null)
      {
        return null;
      }

      poll.Closed = closed;
      poll.ClosedAt = closed ? DateTime.UtcNow : null;
      if (!closed && poll.ClosesAt is not null && poll.ClosesAt <= DateTime.UtcNow)
      {
        // Re-opening a poll whose deadline has passed would do nothing at all: drop the stale deadline.
        poll.ClosesAt = null;
      }

      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return poll;
    }
    finally
    {
      _mutex.Release();
    }
  }

  /// <inheritdoc />
  public async Task<Poll?> VoteAsync(Guid id, PollVote vote, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(vote);
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
      var poll = items.FirstOrDefault(p => p.Id == id);
      if (poll is null)
      {
        return null;
      }

      // One ballot per user: a new vote replaces the previous one rather than stacking on top of it.
      var previous = poll.Votes.FirstOrDefault(v => v.UserId == vote.UserId);
      if (previous is not null)
      {
        poll.Votes.Remove(previous);
      }

      vote.VotedAt = vote.VotedAt == default ? DateTime.UtcNow : vote.VotedAt;
      poll.Votes.Add(vote);

      await SaveAsync(cancellationToken).ConfigureAwait(false);
      return poll;
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
      var removed = items.RemoveAll(p => p.Id == id) > 0;
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

  private async Task<List<Poll>> LoadAsync(CancellationToken cancellationToken)
  {
    if (_cache is not null)
    {
      return _cache;
    }

    _cache = await VersionedJsonFile.ReadAsync<Poll>(_filePath, SchemaVersion, migrate: null, SerializerOptions, cancellationToken).ConfigureAwait(false);
    return _cache;
  }

  private Task SaveAsync(CancellationToken cancellationToken)
    => VersionedJsonFile.WriteAsync(_filePath, SchemaVersion, _cache ?? new List<Poll>(), SerializerOptions, cancellationToken);
}
