using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="NotificationMessages"/>.
/// </summary>
public class NotificationMessagesTests
{
  private static RequestRecord Movie() => new() { MediaType = "movie", Title = "Dune" };

  [Theory]
  [InlineData(NotificationEvent.Created, "New request")]
  [InlineData(NotificationEvent.Approved, "approved")]
  [InlineData(NotificationEvent.Denied, "denied")]
  [InlineData(NotificationEvent.Available, "available")]
  public void Build_IncludesTitle_AndReflectsEvent(NotificationEvent ev, string marker)
  {
    var (subject, body) = NotificationMessages.Build(Movie(), ev);

    Assert.Contains("Dune", subject, System.StringComparison.Ordinal);
    var combined = subject + " " + body;
    Assert.Contains(marker, combined, System.StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Build_UsesShowWording_ForTv()
  {
    var (_, body) = NotificationMessages.Build(new RequestRecord { MediaType = "tv", Title = "Severance" }, NotificationEvent.Created);

    Assert.Contains("show", body, System.StringComparison.Ordinal);
  }
}
