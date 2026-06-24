using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Jellyfin.Plugin.JellyCrowd.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Tasks;

/// <summary>
/// Tests for <see cref="DeletionTask"/>.
/// </summary>
public sealed class DeletionTaskTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;

  public DeletionTaskTests()
  {
    _store = new JsonRequestStore(_path);
  }

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  [Fact]
  public async Task Execute_DeletesDueMedia_AndRemovesRequest()
  {
    var id = await SeedFlaggedAsync("item-abc");
    var deleter = new RecordingDeleter();
    var dispatcher = new RecordingDispatcher();
    var task = new DeletionTask(_store, deleter, dispatcher, new RecordingNotificationService(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Contains("item-abc", deleter.Deleted);
    // Unowned deletion must also purge the download backend (Radarr/Sonarr) so a re-request is clean.
    Assert.Contains(id, dispatcher.Purged);
    Assert.Null(await _store.GetByIdAsync(id, CancellationToken.None));
  }

  [Fact]
  public async Task Execute_KeepsMedia_WhenRetentionNotElapsed()
  {
    var id = await SeedFlaggedAsync("item-xyz");
    var deleter = new RecordingDeleter();
    var task = new DeletionTask(_store, deleter, new RecordingDispatcher(), new RecordingNotificationService(), () => new PluginConfiguration { DeletionRetentionHours = 1_000_000 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Empty(deleter.Deleted);
    Assert.NotNull(await _store.GetByIdAsync(id, CancellationToken.None));
  }

  [Fact]
  public async Task Execute_KeepsRequest_WhenBackendPurgeFails()
  {
    // N18: a failed backend purge (e.g. backend down) must not finalize the deletion — keep the
    // request flagged and the media in place so it retries next run.
    var id = await SeedFlaggedAsync("item-fail");
    var deleter = new RecordingDeleter();
    var dispatcher = new RecordingDispatcher(purgeSucceeds: false);
    var task = new DeletionTask(_store, deleter, dispatcher, new RecordingNotificationService(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Contains(id, dispatcher.Purged);       // purge was attempted
    Assert.Empty(deleter.Deleted);                // media left in place
    Assert.NotNull(await _store.GetByIdAsync(id, CancellationToken.None)); // request kept for retry
  }

  private async Task<Guid> SeedFlaggedAsync(string itemId)
  {
    var user = Guid.NewGuid();
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "X" },
      CancellationToken.None);
    await _store.MarkAvailableAsync(created.Id, itemId, CancellationToken.None);
    await _store.RequestDeletionAsync(created.Id, user, CancellationToken.None);
    return created.Id;
  }

  private sealed class RecordingDeleter : IMediaDeleter
  {
    public List<string> Deleted { get; } = new();

    public bool Delete(string jellyfinItemId)
    {
      Deleted.Add(jellyfinItemId);
      return true;
    }
  }

  private sealed class RecordingDispatcher : IDownloadDispatcher
  {
    private readonly bool _purgeSucceeds;

    public RecordingDispatcher(bool purgeSucceeds = true) => _purgeSucceeds = purgeSucceeds;

    public List<Guid> Purged { get; } = new();

    public Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task DispatchDueAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TestActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> PurgeAsync(RequestRecord request, CancellationToken cancellationToken)
    {
      Purged.Add(request.Id);
      return Task.FromResult(_purgeSucceeds);
    }

    public Task RetryStuckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> RetryAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(true);
  }
}
