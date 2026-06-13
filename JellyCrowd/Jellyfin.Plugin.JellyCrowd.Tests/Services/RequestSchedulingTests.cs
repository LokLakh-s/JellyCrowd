using System;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestScheduling"/>.
/// </summary>
public class RequestSchedulingTests
{
  private static readonly DateTime Now = new(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void ResolveDesiredAt_FutureRelease_SchedulesAtRelease()
  {
    var result = RequestScheduling.ResolveDesiredAt("2030-01-15", null, Now);
    Assert.Equal(new DateTime(2030, 1, 15, 0, 0, 0, DateTimeKind.Utc), result);
  }

  [Fact]
  public void ResolveDesiredAt_PastRelease_UsesNow()
  {
    var result = RequestScheduling.ResolveDesiredAt("1999-03-30", null, Now);
    Assert.Equal(Now, result);
  }

  [Fact]
  public void ResolveDesiredAt_NoRelease_UsesProvidedDesired()
  {
    var desired = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    Assert.Equal(desired, RequestScheduling.ResolveDesiredAt(null, desired, Now));
  }

  [Fact]
  public void ResolveDesiredAt_DesiredLaterThanRelease_UsesDesired()
  {
    var desired = new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    Assert.Equal(desired, RequestScheduling.ResolveDesiredAt("2030-01-15", desired, Now));
  }

  [Theory]
  [InlineData("2030-01-15")]
  [InlineData("1999-03-30")]
  public void ParseReleaseDate_ParsesAsUtcMidnight(string date)
  {
    var parsed = RequestScheduling.ParseReleaseDate(date);
    Assert.NotNull(parsed);
    Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    Assert.Equal(0, parsed.Value.Hour);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("nope")]
  public void ParseReleaseDate_ReturnsNull_ForInvalid(string? date)
  {
    Assert.Null(RequestScheduling.ParseReleaseDate(date));
  }
}
