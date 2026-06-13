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
    var task = new DeletionTask(_store, deleter, () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Contains("item-abc", deleter.Deleted);
    Assert.Null(await _store.GetByIdAsync(id, CancellationToken.None));
  }

  [Fact]
  public async Task Execute_KeepsMedia_WhenRetentionNotElapsed()
  {
    var id = await SeedFlaggedAsync("item-xyz");
    var deleter = new RecordingDeleter();
    var task = new DeletionTask(_store, deleter, () => new PluginConfiguration { DeletionRetentionHours = 1_000_000 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Empty(deleter.Deleted);
    Assert.NotNull(await _store.GetByIdAsync(id, CancellationToken.None));
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
}
