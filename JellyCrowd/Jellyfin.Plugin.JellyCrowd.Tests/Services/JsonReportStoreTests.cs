using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonReportStore"/>.
/// </summary>
public sealed class JsonReportStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonReportStore _store;

  public JsonReportStoreTests() => _store = new JsonReportStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private static MediaReport New(string title = "T") => new()
  {
    MediaType = "movie",
    TmdbId = 1,
    Title = title,
    UserId = Guid.NewGuid(),
    UserName = "u",
    Message = "broken"
  };

  [Fact]
  public async Task AddAsync_AssignsIdAndTimestamp()
  {
    var added = await _store.AddAsync(New(), CancellationToken.None);

    Assert.NotEqual(Guid.Empty, added.Id);
    Assert.NotEqual(default, added.CreatedAt);
  }

  [Fact]
  public async Task SetResolved_And_Delete()
  {
    var a = await _store.AddAsync(New("a"), CancellationToken.None);

    var resolved = await _store.SetResolvedAsync(a.Id, true, CancellationToken.None);
    Assert.True(resolved!.Resolved);

    Assert.True(await _store.DeleteAsync(a.Id, CancellationToken.None));
    Assert.Empty(await _store.GetAllAsync(CancellationToken.None));
  }
}
