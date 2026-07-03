using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonIntroStore"/> — cache round-trip, persistence, removal and the lazy file path.
/// </summary>
public sealed class JsonIntroStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-intros-" + Guid.NewGuid().ToString("N") + ".json");

  [Fact]
  public void UpsertAndGet_RoundTrips_AndPersistsAcrossInstances()
  {
    var a = Guid.NewGuid();
    var b = Guid.NewGuid();
    var store = new JsonIntroStore(() => _path);

    store.UpsertSeason(new Dictionary<Guid, IntroSegment?>
    {
      [a] = new IntroSegment(600_000_000, 1_400_000_000),
      [b] = new IntroSegment(-1, -1), // "analyzed, no intro" sentinel
    });

    Assert.Equal(600_000_000, store.Get(a)!.StartTicks);
    Assert.Equal(-1, store.Get(b)!.StartTicks);
    Assert.Null(store.Get(Guid.NewGuid()));

    // A fresh instance reads the persisted file.
    var reopened = new JsonIntroStore(() => _path);
    Assert.Equal(1_400_000_000, reopened.Get(a)!.EndTicks);
  }

  [Fact]
  public void UpsertSeason_WithNull_RemovesTheEntry()
  {
    var id = Guid.NewGuid();
    var store = new JsonIntroStore(() => _path);
    store.UpsertSeason(new Dictionary<Guid, IntroSegment?> { [id] = new IntroSegment(1, 2) });
    Assert.NotNull(store.Get(id));

    store.UpsertSeason(new Dictionary<Guid, IntroSegment?> { [id] = null });
    Assert.Null(store.Get(id));
  }

  [Fact]
  public void FilePathProvider_IsNotInvoked_AtConstruction()
  {
    // The provider is resolved before Plugin.Instance exists, so the path must stay untouched until use.
    var invoked = false;
    _ = new JsonIntroStore(() => { invoked = true; return _path; });
    Assert.False(invoked);
  }

  public void Dispose()
  {
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }
}
