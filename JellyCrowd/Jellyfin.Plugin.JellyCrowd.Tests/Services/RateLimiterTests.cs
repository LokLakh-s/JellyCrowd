using System;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RateLimiter"/>.
/// </summary>
public class RateLimiterTests
{
  private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

  [Fact]
  public void AllowsUpToLimit_ThenDenies()
  {
    var limiter = new RateLimiter();
    var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    Assert.True(limiter.TryAcquire("u1", 3, Minute, now));
    Assert.True(limiter.TryAcquire("u1", 3, Minute, now));
    Assert.True(limiter.TryAcquire("u1", 3, Minute, now));
    Assert.False(limiter.TryAcquire("u1", 3, Minute, now)); // 4th within the window
  }

  [Fact]
  public void WindowSlides_AllowsAgainAfterExpiry()
  {
    var limiter = new RateLimiter();
    var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    Assert.True(limiter.TryAcquire("u1", 1, Minute, now));
    Assert.False(limiter.TryAcquire("u1", 1, Minute, now));
    // Just past the window: the earlier hit has aged out.
    Assert.True(limiter.TryAcquire("u1", 1, Minute, now.AddSeconds(61)));
  }

  [Fact]
  public void KeysAreIndependent()
  {
    var limiter = new RateLimiter();
    var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    Assert.True(limiter.TryAcquire("a", 1, Minute, now));
    Assert.True(limiter.TryAcquire("b", 1, Minute, now));
    Assert.False(limiter.TryAcquire("a", 1, Minute, now));
  }

  [Fact]
  public void ZeroOrNegativeMax_Disabled()
  {
    var limiter = new RateLimiter();
    var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    for (var i = 0; i < 1000; i++)
    {
      Assert.True(limiter.TryAcquire("u1", 0, Minute, now));
    }
  }
}
