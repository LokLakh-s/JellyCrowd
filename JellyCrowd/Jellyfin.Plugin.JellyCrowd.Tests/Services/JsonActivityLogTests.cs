using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonActivityLog"/>.
/// </summary>
public sealed class JsonActivityLogTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonActivityLog _log;

  public JsonActivityLogTests() => _log = new JsonActivityLog(_path);

  public void Dispose()
  {
    _log.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  [Fact]
  public async Task LogAsync_ThenQuery_ReturnsNewestFirst()
  {
    await _log.LogAsync("info", "request", "first", CancellationToken.None);
    await _log.LogAsync("error", "download", "second", CancellationToken.None);

    var all = await _log.QueryAsync(null, null, null, null, 100, CancellationToken.None);

    Assert.Equal(2, all.Count);
    Assert.Equal("second", all[0].Message);
    Assert.Equal("first", all[1].Message);
  }

  [Fact]
  public async Task QueryAsync_FiltersByCategoryLevelAndTerm()
  {
    await _log.LogAsync("info", "request", "Approved The Matrix", CancellationToken.None);
    await _log.LogAsync("error", "download", "Dispatch failed for Dune", CancellationToken.None);
    await _log.LogAsync("info", "download", "Dispatched Dune to radarr", CancellationToken.None);

    var byCategory = await _log.QueryAsync(null, "download", null, null, 100, CancellationToken.None);
    Assert.Equal(2, byCategory.Count);

    var byLevel = await _log.QueryAsync(null, null, "error", null, 100, CancellationToken.None);
    Assert.Single(byLevel);
    Assert.Equal("Dispatch failed for Dune", byLevel[0].Message);

    var byTerm = await _log.QueryAsync("matrix", null, null, null, 100, CancellationToken.None);
    Assert.Single(byTerm);
  }

  [Fact]
  public async Task QueryAsync_FiltersByUser()
  {
    await _log.LogAsync("info", "user", "Alice added Dune", "Alice", CancellationToken.None);
    await _log.LogAsync("info", "user", "Bob added Arrival", "Bob", CancellationToken.None);
    await _log.LogAsync("info", "system", "cache warmed", CancellationToken.None); // no user

    var byUser = await _log.QueryAsync(null, null, null, "Alice", 100, CancellationToken.None);
    Assert.Single(byUser);
    Assert.Equal("Alice added Dune", byUser[0].Message);
    Assert.Equal("Alice", byUser[0].User);

    // Case-insensitive, and entries with no user are excluded from a user-filtered query.
    var byBob = await _log.QueryAsync(null, null, null, "bob", 100, CancellationToken.None);
    Assert.Single(byBob);
    Assert.Equal("Bob", byBob[0].User);
  }

  [Fact]
  public async Task QueryAsync_RespectsLimit()
  {
    for (var i = 0; i < 5; i++)
    {
      await _log.LogAsync("info", "system", "entry " + i, CancellationToken.None);
    }

    var limited = await _log.QueryAsync(null, null, null, null, 2, CancellationToken.None);
    Assert.Equal(2, limited.Count);
  }

  [Fact]
  public async Task LogAsync_DefaultsBlankLevelAndCategory()
  {
    await _log.LogAsync(" ", " ", "msg", CancellationToken.None);

    var all = await _log.QueryAsync(null, null, null, null, 100, CancellationToken.None);
    Assert.Single(all);
    Assert.Equal("info", all[0].Level);
    Assert.Equal("system", all[0].Category);
  }
}
