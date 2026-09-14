using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestCreationGate"/> — one request creation at a time per user.
/// </summary>
public class RequestCreationGateTests
{
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

  [Fact]
  public async Task TheSameUser_WaitsForTheCreationInProgress()
  {
    var gate = new RequestCreationGate();
    var user = Guid.NewGuid();

    var first = await gate.EnterAsync(user, CancellationToken.None);
    var second = gate.EnterAsync(user, CancellationToken.None);
    Assert.False(second.IsCompleted); // the second request cannot check duplicates until the first is recorded

    first.Dispose();
    (await second.WaitAsync(Timeout)).Dispose();
  }

  [Fact]
  public async Task DifferentUsers_NeverWaitOnEachOther()
  {
    var gate = new RequestCreationGate();

    using var first = await gate.EnterAsync(Guid.NewGuid(), CancellationToken.None);
    using var other = await gate.EnterAsync(Guid.NewGuid(), CancellationToken.None).WaitAsync(Timeout);
  }

  [Fact]
  public async Task DisposingTwice_ReleasesOnlyOnce()
  {
    var gate = new RequestCreationGate();
    var user = Guid.NewGuid();

    var first = await gate.EnterAsync(user, CancellationToken.None);
    first.Dispose();
    first.Dispose(); // must not over-release and let two creations in at once

    using var second = await gate.EnterAsync(user, CancellationToken.None).WaitAsync(Timeout);
    var third = gate.EnterAsync(user, CancellationToken.None);
    Assert.False(third.IsCompleted);
    second.Dispose();
    (await third.WaitAsync(Timeout)).Dispose();
  }
}
