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

  [Fact]
  public void BuildAvailableBatch_SummarizesEpisodeCount_AndSeason()
  {
    var rep = new RequestRecord { MediaType = "tv", Title = "Rick and Morty", Season = 9 };

    var (subject, body) = NotificationMessages.BuildAvailableBatch(rep, new[] { 1, 2, 3, 4, 5, 6 });

    Assert.Equal("Now available: Rick and Morty (Season 9)", subject);
    Assert.Contains("6 episodes", body, System.StringComparison.Ordinal);
    Assert.Contains("Rick and Morty (Season 9)", body, System.StringComparison.Ordinal);
  }

  [Theory]
  [InlineData(new[] { 1, 2, 3, 4, 5, 6 }, "1–6")]
  [InlineData(new[] { 1, 2, 4, 5 }, "1–2, 4–5")]
  [InlineData(new[] { 1, 3, 5 }, "1, 3, 5")]
  [InlineData(new[] { 5, 1, 2 }, "1–2, 5")]
  public void BuildAvailableBatch_CollapsesContiguousEpisodesIntoRanges(int[] episodes, string expectedRange)
  {
    var rep = new RequestRecord { MediaType = "tv", Title = "Show", Season = 1 };

    var (_, body) = NotificationMessages.BuildAvailableBatch(rep, episodes);

    Assert.Contains("(episodes " + expectedRange + ").", body, System.StringComparison.Ordinal);
  }
}
