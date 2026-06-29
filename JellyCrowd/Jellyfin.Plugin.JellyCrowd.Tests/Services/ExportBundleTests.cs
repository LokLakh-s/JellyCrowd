using System;
using System.IO;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ExportBundle"/>.
/// </summary>
public sealed class ExportBundleTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid().ToString());

  public ExportBundleTests() => Directory.CreateDirectory(_dir);

  public void Dispose()
  {
    if (Directory.Exists(_dir))
    {
      Directory.Delete(_dir, recursive: true);
    }
  }

  [Fact]
  public void Build_IncludesPresentStoresAndEmptyArraysForMissing()
  {
    File.WriteAllText(Path.Combine(_dir, "requests.json"), """[{"Title":"X"}]""");

    var bundle = ExportBundle.Build(_dir);

    Assert.Single(bundle["requests"]!.AsArray());
    Assert.Empty(bundle["watchlist"]!.AsArray());   // missing file -> empty array
    Assert.NotNull(bundle["notifications"]);
    Assert.NotNull(bundle["prefs"]);
  }

  [Fact]
  public void Build_CorruptStore_FallsBackToEmptyArray()
  {
    File.WriteAllText(Path.Combine(_dir, "requests.json"), "{ not json");

    var bundle = ExportBundle.Build(_dir);

    Assert.Empty(bundle["requests"]!.AsArray());
  }
}
