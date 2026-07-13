using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="VersionedJsonFile"/>, focused on resilience to a corrupt store file.
/// </summary>
public sealed class VersionedJsonFileTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-vjf-" + Guid.NewGuid().ToString("N") + ".json");
  private static readonly JsonSerializerOptions Options = new();

  public void Dispose()
  {
    foreach (var p in new[] { _path, _path + ".corrupt", _path + ".tmp" })
    {
      if (File.Exists(p))
      {
        File.Delete(p);
      }
    }
  }

  private Task<List<string>> ReadAsync() => VersionedJsonFile.ReadAsync<string>(_path, 1, null, Options, CancellationToken.None);

  [Fact]
  public async Task ReadAsync_MissingFile_ReturnsEmpty()
  {
    Assert.Empty(await ReadAsync());
  }

  [Fact]
  public async Task RoundTrips_ThroughTheEnvelope()
  {
    await VersionedJsonFile.WriteAsync(_path, 1, new List<string> { "a", "b" }, Options, CancellationToken.None);
    Assert.Equal(new[] { "a", "b" }, await ReadAsync());
  }

  [Fact]
  public async Task ReadAsync_CorruptFile_DoesNotThrow_QuarantinesAndStartsFresh()
  {
    await File.WriteAllTextAsync(_path, "{ this is not valid json ]");

    // Before the fix this threw, so every store read 500'd forever.
    var items = await ReadAsync();

    Assert.Empty(items);
    Assert.False(File.Exists(_path), "the corrupt file should have been moved aside");
    Assert.True(File.Exists(_path + ".corrupt"), "the corrupt bytes should be kept for recovery");

    // The store is usable again: the next write recreates a clean file that reads back.
    await VersionedJsonFile.WriteAsync(_path, 1, new List<string> { "recovered" }, Options, CancellationToken.None);
    Assert.Equal(new[] { "recovered" }, await ReadAsync());
  }

  [Fact]
  public async Task ReadAsync_LegacyBareArray_StillReads()
  {
    await File.WriteAllTextAsync(_path, "[\"x\",\"y\"]");
    Assert.Equal(new[] { "x", "y" }, await ReadAsync());
  }
}
