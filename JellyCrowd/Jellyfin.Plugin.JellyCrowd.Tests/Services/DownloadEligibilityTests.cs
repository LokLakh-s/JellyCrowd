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

  [Fact]
  public void IsAwaitingRelease_TrueOnlyWhileTheDesiredTimeIsAhead()
  {
    Assert.True(DownloadEligibility.IsAwaitingRelease(new RequestRecord { DesiredAt = Now.AddDays(1) }, Now));
    Assert.False(DownloadEligibility.IsAwaitingRelease(new RequestRecord { DesiredAt = Now.AddDays(-1) }, Now));
    Assert.False(DownloadEligibility.IsAwaitingRelease(new RequestRecord { DesiredAt = null }, Now));
  }

  [Fact]
  public void SearchStartedAt_IsTheDispatch_NotTheRequest()
  {
    // Requested five weeks before its release, sent on release day: the search starts on release day.
    var request = new RequestRecord { RequestedAt = Now.AddDays(-35), DesiredAt = Now, DispatchedAt = Now.AddMinutes(4) };
    Assert.Equal(Now.AddMinutes(4), DownloadEligibility.SearchStartedAt(request));
  }

  [Fact]
  public void SearchStartedAt_IsTheRelease_WhenItMovedPastTheDispatch()
  {
    // Sent before its air date was known; the date learned since is later.
    var request = new RequestRecord { RequestedAt = Now.AddDays(-8), DispatchedAt = Now.AddDays(-7), DesiredAt = Now.AddDays(7) };
    Assert.Equal(Now.AddDays(7), DownloadEligibility.SearchStartedAt(request));
  }

  [Fact]
  public void SearchStartedAt_FallsBackToTheRequest_WhenNeverDispatched()
  {
    var request = new RequestRecord { RequestedAt = Now.AddDays(-2) };
    Assert.Equal(Now.AddDays(-2), DownloadEligibility.SearchStartedAt(request));
  }

  [Fact]
  public void IsNotFoundPremature_True_ForAStampSetOnTheDayItWasSent()
  {
    var request = new RequestRecord { RequestedAt = Now.AddDays(-38), DesiredAt = Now.AddHours(-12), DispatchedAt = Now.AddHours(-12), NotFoundNotifiedAt = Now.AddHours(-12) };
    Assert.True(DownloadEligibility.IsNotFoundPremature(request, Now, TimeSpan.FromDays(14)));
  }

  [Fact]
  public void IsNotFoundPremature_False_ForAStampAfterTheFullWindow()
  {
    var request = new RequestRecord { DispatchedAt = Now.AddDays(-15), NotFoundNotifiedAt = Now.AddDays(-1) };
    Assert.False(DownloadEligibility.IsNotFoundPremature(request, Now, TimeSpan.FromDays(14)));
  }

  [Fact]
  public void IsNotFoundPremature_False_WhenTheWindowHasRunOutSince()
  {
    // Stamped too early, but the full window has elapsed by now: the warning stands, it is not sent twice.
    var request = new RequestRecord { DispatchedAt = Now.AddDays(-20), NotFoundNotifiedAt = Now.AddDays(-13) };
    Assert.False(DownloadEligibility.IsNotFoundPremature(request, Now, TimeSpan.FromDays(14)));
  }

  [Fact]
  public void IsNotFoundPremature_False_WhenNotStamped()
  {
    Assert.False(DownloadEligibility.IsNotFoundPremature(new RequestRecord { DispatchedAt = Now }, Now, TimeSpan.FromDays(14)));
  }
}
