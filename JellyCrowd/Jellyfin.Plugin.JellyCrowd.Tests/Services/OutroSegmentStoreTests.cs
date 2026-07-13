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
/// Tests for <see cref="JsonOutroSegmentStore"/> and for the pre-write backup that lets a corrupt store
/// recover instead of silently starting empty.
/// </summary>
public sealed class OutroSegmentStoreTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), "jc-oss-" + Guid.NewGuid().ToString("N"));

  public OutroSegmentStoreTests() => Directory.CreateDirectory(_dir);

  public void Dispose()
  {
    if (Directory.Exists(_dir))
    {
      Directory.Delete(_dir, recursive: true);
    }
  }

  [Fact]
  public void RoundTrips_AndTellsNotAnalyzedApartFromNoCredits()
  {
    var path = Path.Combine(_dir, "outro-segments.json");
    var withCredits = Guid.NewGuid();
    var noCredits = Guid.NewGuid();
    var store = new JsonOutroSegmentStore(() => path);

    store.UpsertSeason(new Dictionary<Guid, OutroRegion>
    {
      [withCredits] = new(1200 * 10_000_000L, 1290 * 10_000_000L),
      [noCredits] = new(-1, -1) // analyzed, but this season has no recurring credits
    });

    // Re-read from disk through a fresh store: the values must survive.
    var reloaded = new JsonOutroSegmentStore(() => path);
    Assert.Equal(1200 * 10_000_000L, reloaded.Get(withCredits)!.StartTicks);

    // The sentinel is NOT "no entry": it means "analyzed, nothing found" (the provider then falls back to
    // the brightness/silence heuristic), whereas a missing entry means "not analyzed yet".
    Assert.Equal(-1, reloaded.Get(noCredits)!.StartTicks);
    Assert.Null(reloaded.Get(Guid.NewGuid()));
  }

  [Fact]
  public async Task CorruptStore_RecoversFromThePreWriteBackup()
  {
    var path = Path.Combine(_dir, "requests.json");
    var options = new JsonSerializerOptions();

    // Two writes: the second one leaves a .bak holding the first's contents.
    await VersionedJsonFile.WriteAsync(path, 1, new List<string> { "ownership" }, options, CancellationToken.None);
    await VersionedJsonFile.WriteAsync(path, 1, new List<string> { "ownership", "more-ownership" }, options, CancellationToken.None);
    Assert.True(File.Exists(path + ".bak"));

    // Now the live file is destroyed (truncated write, tampering...).
    await File.WriteAllTextAsync(path, "{ truncated");

    var items = await VersionedJsonFile.ReadAsync<string>(path, 1, null, options, CancellationToken.None);

    // Rather than silently losing everything, the previous good copy is recovered and reinstated. For the
    // request store this is every user's media ownership and quota — nothing else in Jellyfin knows it.
    Assert.Equal(new[] { "ownership" }, items);
    Assert.True(File.Exists(path + ".corrupt"), "the corrupt bytes are kept for inspection");
    Assert.Equal(new[] { "ownership" }, await VersionedJsonFile.ReadAsync<string>(path, 1, null, options, CancellationToken.None));
  }

  [Fact]
  public async Task CorruptStore_WithNoBackup_StartsEmptyRatherThanThrowing()
  {
    var path = Path.Combine(_dir, "fresh.json");
    await File.WriteAllTextAsync(path, "not json at all ]");

    Assert.Empty(await VersionedJsonFile.ReadAsync<string>(path, 1, null, new JsonSerializerOptions(), CancellationToken.None));
  }
}
