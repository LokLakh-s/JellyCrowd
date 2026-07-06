using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonOutroStore"/> and <see cref="OutroAnalysis.Matches"/> — the memory that stops
/// the Media Segment Scan re-running ffmpeg on already-analyzed items.
/// </summary>
public sealed class JsonOutroStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-outros-" + Guid.NewGuid().ToString("N") + ".json");

  [Fact]
  public void SetAndGet_RoundTrips_AndPersistsAcrossInstances()
  {
    var withOutro = Guid.NewGuid();
    var noOutro = Guid.NewGuid();
    var store = new JsonOutroStore(() => _path);

    store.Set(withOutro, new OutroAnalysis(1000, 200, 3000, "sig", new List<OutroRegion> { new(600_000_000, 1_400_000_000) }));
    store.Set(noOutro, new OutroAnalysis(50, 60, 70, "sig", Array.Empty<OutroRegion>()));

    Assert.Single(store.Get(withOutro)!.Regions);
    Assert.Equal(1_400_000_000, store.Get(withOutro)!.Regions[0].EndTicks);
    Assert.Empty(store.Get(noOutro)!.Regions); // "analyzed, no outro" is a real remembered result
    Assert.Null(store.Get(Guid.NewGuid()));

    // A fresh instance reads the persisted file.
    var reopened = new JsonOutroStore(() => _path);
    Assert.Equal(600_000_000, reopened.Get(withOutro)!.Regions[0].StartTicks);
    Assert.NotNull(reopened.Get(noOutro));
  }

  [Fact]
  public void Set_ReplacesExistingEntry()
  {
    var id = Guid.NewGuid();
    var store = new JsonOutroStore(() => _path);
    store.Set(id, new OutroAnalysis(1, 1, 1, "old", Array.Empty<OutroRegion>()));
    store.Set(id, new OutroAnalysis(2, 2, 2, "new", new List<OutroRegion> { new(10, 20) }));

    Assert.Equal("new", store.Get(id)!.OptionsSignature);
    Assert.Single(store.Get(id)!.Regions);
  }

  [Fact]
  public void Remove_ForgetsTheEntry_SoItIsReanalyzed()
  {
    var id = Guid.NewGuid();
    var store = new JsonOutroStore(() => _path);
    store.Set(id, new OutroAnalysis(1, 1, 1, "sig", Array.Empty<OutroRegion>()));
    Assert.NotNull(store.Get(id));

    store.Remove(id);
    Assert.Null(store.Get(id));

    // The removal is persisted.
    Assert.Null(new JsonOutroStore(() => _path).Get(id));
  }

  [Fact]
  public void Matches_TrueOnlyWhenFileAndTuningUnchanged()
  {
    var analysis = new OutroAnalysis(1000, 200, 3000, "sig", Array.Empty<OutroRegion>());

    Assert.True(analysis.Matches(1000, 200, 3000, "sig"));
    Assert.False(analysis.Matches(1001, 200, 3000, "sig")); // file size changed (re-encode/remux)
    Assert.False(analysis.Matches(1000, 201, 3000, "sig")); // file modified time changed
    Assert.False(analysis.Matches(1000, 200, 3001, "sig")); // runtime changed
    Assert.False(analysis.Matches(1000, 200, 3000, "sig2")); // detection tuning changed
  }

  [Fact]
  public void FilePathProvider_IsNotInvoked_AtConstruction()
  {
    // The provider is resolved before Plugin.Instance exists, so the path must stay untouched until use.
    var invoked = false;
    _ = new JsonOutroStore(() => { invoked = true; return _path; });
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
