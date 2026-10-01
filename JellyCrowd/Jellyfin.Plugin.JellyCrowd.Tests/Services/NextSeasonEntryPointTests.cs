using System;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="NextSeasonEntryPoint"/>: what it reads from a played item.
/// </summary>
public class NextSeasonEntryPointTests
{
  private static (Episode Episode, ILibraryManager Library) Played(int? season, int? number, string? tmdb = "1396")
  {
    var series = new Series { Id = Guid.NewGuid(), Name = "Breaking Bad" };
    if (tmdb is not null)
    {
      series.SetProviderId(MetadataProvider.Tmdb, tmdb);
    }

    var library = new Mock<ILibraryManager>();
    library.Setup(l => l.GetItemById(series.Id)).Returns(series);
    var episode = new Episode { Id = Guid.NewGuid(), SeriesId = series.Id, ParentIndexNumber = season, IndexNumber = number };
    return (episode, library.Object);
  }

  [Fact]
  public void ResolveEpisode_ReadsTheShowAndTheNumbers()
  {
    var (episode, library) = Played(1, 8);

    var resolved = NextSeasonEntryPoint.ResolveEpisode(episode, library);

    Assert.Equal((1396, "Breaking Bad", 1, 8), resolved);
  }

  [Theory]
  [InlineData(0, 3)]       // a special
  [InlineData(null, 3)]    // no season number
  [InlineData(1, null)]    // no episode number
  public void ResolveEpisode_SpecialsAndUnnumbered_AreIgnored(int? season, int? number)
  {
    var (episode, library) = Played(season, number);

    Assert.Null(NextSeasonEntryPoint.ResolveEpisode(episode, library));
  }

  [Fact]
  public void ResolveEpisode_ShowWithoutATmdbId_IsIgnored()
  {
    var (episode, library) = Played(1, 8, tmdb: null);

    Assert.Null(NextSeasonEntryPoint.ResolveEpisode(episode, library));
  }

  [Fact]
  public void ResolveEpisode_NotAnEpisode_IsIgnored()
  {
    Assert.Null(NextSeasonEntryPoint.ResolveEpisode(new Movie(), Mock.Of<ILibraryManager>()));
    Assert.Null(NextSeasonEntryPoint.ResolveEpisode(null, Mock.Of<ILibraryManager>()));
  }
}
