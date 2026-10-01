using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonAutoRequestLedger"/>.
/// </summary>
public sealed class JsonAutoRequestLedgerTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");

  public void Dispose()
  {
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  [Fact]
  public async Task Record_ThenContains_PerUserShowAndSeason()
  {
    using var ledger = new JsonAutoRequestLedger(_path);
    var user = Guid.NewGuid();

    await ledger.RecordAsync(user, 1396, 2, CancellationToken.None);

    Assert.True(await ledger.ContainsAsync(user, 1396, 2, CancellationToken.None));
    Assert.False(await ledger.ContainsAsync(user, 1396, 3, CancellationToken.None));
    Assert.False(await ledger.ContainsAsync(user, 1397, 2, CancellationToken.None));
    Assert.False(await ledger.ContainsAsync(Guid.NewGuid(), 1396, 2, CancellationToken.None));
  }

  [Fact]
  public async Task Record_SurvivesARestart()
  {
    var user = Guid.NewGuid();
    using (var ledger = new JsonAutoRequestLedger(_path))
    {
      await ledger.RecordAsync(user, 1396, 2, CancellationToken.None);
      await ledger.RecordAsync(user, 1396, 2, CancellationToken.None); // idempotent
    }

    using var reopened = new JsonAutoRequestLedger(_path);
    Assert.True(await reopened.ContainsAsync(user, 1396, 2, CancellationToken.None));
  }

  [Fact]
  public async Task Record_PastTheCap_DropsTheOldest()
  {
    using var ledger = new JsonAutoRequestLedger(_path, maxEntries: 3);
    var user = Guid.NewGuid();
    for (var i = 0; i < 4; i++)
    {
      await ledger.RecordAsync(user, i, 1, CancellationToken.None);
    }

    Assert.False(await ledger.ContainsAsync(user, 0, 1, CancellationToken.None));
    Assert.True(await ledger.ContainsAsync(user, 3, 1, CancellationToken.None));
  }
}
