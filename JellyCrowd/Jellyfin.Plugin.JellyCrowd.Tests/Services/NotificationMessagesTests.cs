using System;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="NotificationMessages"/>. The wording comes from the shipped catalogs, so these
/// exercise the real English and French text rather than a stub.
/// </summary>
public class NotificationMessagesTests
{
  private static readonly Func<string, string> En = ServerStrings.For("en");
  private static readonly Func<string, string> Fr = ServerStrings.For("fr");

  private static RequestRecord Movie() => new() { MediaType = "movie", Title = "Dune" };

  [Theory]
  [InlineData(NotificationEvent.Created, "New request")]
  [InlineData(NotificationEvent.Approved, "approved")]
  [InlineData(NotificationEvent.Denied, "denied")]
  [InlineData(NotificationEvent.Available, "available")]
  public void Build_IncludesTitle_AndReflectsEvent(NotificationEvent ev, string marker)
  {
    var (subject, body) = NotificationMessages.Build(Movie(), ev, En);

    Assert.Contains("Dune", subject, StringComparison.Ordinal);
    var combined = subject + " " + body;
    Assert.Contains(marker, combined, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Build_UsesShowWording_ForTv()
  {
    var (_, body) = NotificationMessages.Build(new RequestRecord { MediaType = "tv", Title = "Severance" }, NotificationEvent.Created, En);

    Assert.Contains("show", body, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData(NotificationEvent.Created, "Nouvelle demande")]
  [InlineData(NotificationEvent.Approved, "approuvée")]
  [InlineData(NotificationEvent.Denied, "refusée")]
  [InlineData(NotificationEvent.Available, "disponible")]
  [InlineData(NotificationEvent.Failed, "traiter")]
  public void Build_InFrench_ComesFromTheFrenchCatalog(NotificationEvent ev, string marker)
  {
    var (subject, body) = NotificationMessages.Build(Movie(), ev, Fr);

    Assert.Contains("Dune", subject + body, StringComparison.Ordinal);
    Assert.Contains(marker, subject + " " + body, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Build_InFrench_TranslatesTheMediaKindToo()
  {
    // "film" / "série", not "a new movie request" with a French sentence around it.
    var (_, movie) = NotificationMessages.Build(Movie(), NotificationEvent.Created, Fr);
    var (_, show) = NotificationMessages.Build(new RequestRecord { MediaType = "tv", Title = "Severance" }, NotificationEvent.Created, Fr);

    Assert.Contains("film", movie, StringComparison.Ordinal);
    Assert.Contains("série", show, StringComparison.Ordinal);
    Assert.DoesNotContain("movie", movie, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void TitleOf_UsesTheLanguagesOwnSeasonWording()
  {
    var request = new RequestRecord { MediaType = "tv", Title = "Rick and Morty", Season = 9 };

    Assert.Equal("Rick and Morty (Season 9)", NotificationMessages.TitleOf(request, En));
    Assert.Equal("Rick and Morty (saison 9)", NotificationMessages.TitleOf(request, Fr));
    Assert.Equal("Dune", NotificationMessages.TitleOf(Movie(), Fr));
  }

  [Fact]
  public void BuildAvailableBatch_SummarizesEpisodeCount_AndSeason()
  {
    var rep = new RequestRecord { MediaType = "tv", Title = "Rick and Morty", Season = 9 };

    var (subject, body) = NotificationMessages.BuildAvailableBatch(rep, new[] { 1, 2, 3, 4, 5, 6 }, En);

    Assert.Equal("Now available: Rick and Morty (Season 9)", subject);
    Assert.Contains("6 episodes", body, StringComparison.Ordinal);
    Assert.Contains("Rick and Morty (Season 9)", body, StringComparison.Ordinal);
  }

  [Fact]
  public void BuildAvailableBatch_InFrench()
  {
    var rep = new RequestRecord { MediaType = "tv", Title = "Rick and Morty", Season = 9 };

    var (subject, body) = NotificationMessages.BuildAvailableBatch(rep, new[] { 1, 2, 3 }, Fr);

    Assert.Equal("Désormais disponible : Rick and Morty (saison 9)", subject);
    Assert.Contains("3 épisodes", body, StringComparison.Ordinal);
    Assert.Contains("(épisodes 1–3)", body, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData(new[] { 1, 2, 3, 4, 5, 6 }, "1–6")]
  [InlineData(new[] { 1, 2, 4, 5 }, "1–2, 4–5")]
  [InlineData(new[] { 1, 3, 5 }, "1, 3, 5")]
  [InlineData(new[] { 5, 1, 2 }, "1–2, 5")]
  public void BuildAvailableBatch_CollapsesContiguousEpisodesIntoRanges(int[] episodes, string expectedRange)
  {
    var rep = new RequestRecord { MediaType = "tv", Title = "Show", Season = 1 };

    var (_, body) = NotificationMessages.BuildAvailableBatch(rep, episodes, En);

    Assert.Contains("(episodes " + expectedRange + ").", body, StringComparison.Ordinal);
  }
}
