using System;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="DownloadEligibility"/>.
/// </summary>
public class DownloadEligibilityTests
{
  private static readonly DateTime Now = new(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void IsDue_True_WhenApprovedNotDispatchedAndDesiredReached()
  {
    var request = new RequestRecord { Status = RequestStatus.Approved, DesiredAt = Now.AddMinutes(-1) };
    Assert.True(DownloadEligibility.IsDue(request, Now));
  }

  [Fact]
  public void IsDue_True_WhenDesiredIsNull()
  {
    var request = new RequestRecord { Status = RequestStatus.Approved, DesiredAt = null };
    Assert.True(DownloadEligibility.IsDue(request, Now));
  }

  [Fact]
  public void IsDue_False_WhenNotApproved()
  {
    Assert.False(DownloadEligibility.IsDue(new RequestRecord { Status = RequestStatus.Pending }, Now));
    Assert.False(DownloadEligibility.IsDue(new RequestRecord { Status = RequestStatus.Available }, Now));
  }

  [Fact]
  public void IsDue_False_WhenAlreadyDispatched()
  {
    var request = new RequestRecord { Status = RequestStatus.Approved, DispatchedAt = Now.AddHours(-1) };
    Assert.False(DownloadEligibility.IsDue(request, Now));
  }

  [Fact]
  public void IsDue_False_WhenDesiredInFuture()
  {
    var request = new RequestRecord { Status = RequestStatus.Approved, DesiredAt = Now.AddDays(1) };
    Assert.False(DownloadEligibility.IsDue(request, Now));
  }
}
