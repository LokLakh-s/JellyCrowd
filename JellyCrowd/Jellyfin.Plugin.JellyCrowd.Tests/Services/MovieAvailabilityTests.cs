using System;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="MovieAvailability"/>: the home release, by the rule Radarr applies to the
/// "released" availability Jelly Crowd adds movies with.
/// </summary>
public class MovieAvailabilityTests
{
  private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

  private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void HomeRelease_TheEarlierOfDigitalAndPhysical()
  {
    var release = new MovieRelease { Theatrical = Day(2026, 6, 26), Digital = Day(2026, 10, 29), Physical = Day(2026, 11, 4) };
    Assert.Equal(Day(2026, 10, 29), MovieAvailability.HomeRelease(release));
  }

  [Fact]
  public void HomeRelease_OnlyACinemaRelease_CountsNinetyDaysAfter()
  {
    Assert.Equal(Day(2026, 12, 15), MovieAvailability.HomeRelease(new MovieRelease { Theatrical = Day(2026, 9, 16) }));
  }

  [Fact]
  public void HomeRelease_NoDate_IsUnknown()
  {
    Assert.Null(MovieAvailability.HomeRelease(new MovieRelease { Status = "Released" }));
  }

  [Theory]
  [InlineData("Post Production", true)]
  [InlineData("In Production", true)]
  [InlineData("Released", false)]
  [InlineData(null, false)] // unknown status: no reason to hold it back
  public void IsUnannounced_NotOutAndNoDate(string? status, bool expected)
  {
    Assert.Equal(expected, MovieAvailability.IsUnannounced(new MovieRelease { Status = status }));
  }

  [Fact]
  public void IsUnannounced_False_OnceADateIsKnown()
  {
    Assert.False(MovieAvailability.IsUnannounced(new MovieRelease { Status = "Post Production", Theatrical = Day(2027, 1, 1) }));
  }

  [Fact]
  public void Reschedule_StillInCinemas_DefersToTheHomeRelease()
  {
    var request = new RequestRecord { DesiredAt = Day(2026, 9, 4) };
    var release = new MovieRelease { Theatrical = Day(2026, 6, 26), Digital = Day(2026, 10, 29) };

    Assert.Equal(Day(2026, 10, 29), MovieAvailability.Reschedule(request, release, Now));
  }

  [Fact]
  public void Reschedule_AlreadyOutAndDue_IsLeftAlone()
  {
    var request = new RequestRecord { DesiredAt = Day(2026, 8, 19) };
    Assert.Null(MovieAvailability.Reschedule(request, new MovieRelease { Theatrical = Day(2014, 7, 26) }, Now));
  }

  [Fact]
  public void Reschedule_HomeReleaseMovedEarlier_MakesItDueNow()
  {
    var request = new RequestRecord { DesiredAt = Day(2026, 12, 1) };
    Assert.Equal(Now, MovieAvailability.Reschedule(request, new MovieRelease { Digital = Day(2026, 10, 1) }, Now));
  }

  [Fact]
  public void Reschedule_Unchanged_ReturnsNull()
  {
    var request = new RequestRecord { DesiredAt = Day(2026, 10, 29) };
    Assert.Null(MovieAvailability.Reschedule(request, new MovieRelease { Digital = Day(2026, 10, 29) }, Now));
  }
}
