using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="LibraryMatcher"/>.
/// </summary>
public class LibraryMatcherTests
{
  [Fact]
  public void Exists_ReturnsTrue_WhenLibraryHasMatch()
  {
    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
      .Returns(new List<BaseItem> { new Movie() });

    var matcher = new LibraryMatcher(manager.Object);

    Assert.True(matcher.Exists("movie", 123));
  }

  [Fact]
  public void FindItemId_ReturnsHexId_WhenMatched()
  {
    var id = Guid.NewGuid();
    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
      .Returns(new List<BaseItem> { new Movie { Id = id } });

    var matcher = new LibraryMatcher(manager.Object);

    Assert.Equal(id.ToString("N"), matcher.FindItemId("movie", 1));
  }

  [Fact]
  public void FindItemId_ReturnsNull_WhenEmpty()
  {
    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem>());

    Assert.Null(new LibraryMatcher(manager.Object).FindItemId("movie", 1));
  }

  [Fact]
  public void FindSeasonItemId_ResolvesSeasonByTmdbAndIndex()
  {
    var seasonId = Guid.NewGuid();
    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes != null && q.IncludeItemTypes.Contains(BaseItemKind.Series))))
      .Returns(new List<BaseItem> { new Series { Id = Guid.NewGuid() } });
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes != null && q.IncludeItemTypes.Contains(BaseItemKind.Season))))
      .Returns(new List<BaseItem> { new Season { Id = Guid.NewGuid(), IndexNumber = 1 }, new Season { Id = seasonId, IndexNumber = 2 } });

    var matcher = new LibraryMatcher(manager.Object);

    // Resolves the exact season (index 2) under the TMDB-matched series — the item the deletion targets.
    Assert.Equal(seasonId.ToString("N"), matcher.FindSeasonItemId(1396, 2));
  }

  [Fact]
  public void FindSeasonItemId_ReturnsNull_WhenSeriesMissing()
  {
    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem>());

    Assert.Null(new LibraryMatcher(manager.Object).FindSeasonItemId(1, 1));
  }

  [Fact]
  public void Exists_ReturnsFalse_WhenLibraryEmpty()
  {
    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
      .Returns(new List<BaseItem>());

    var matcher = new LibraryMatcher(manager.Object);

    Assert.False(matcher.Exists("tv", 99));
  }

  [Fact]
  public void Exists_ReturnsFalse_AndSkipsQuery_ForUnknownMediaType()
  {
    var manager = new Mock<ILibraryManager>();
    var matcher = new LibraryMatcher(manager.Object);

    Assert.False(matcher.Exists("book", 1));
    manager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
  }

  [Theory]
  [InlineData("movie", BaseItemKind.Movie)]
  [InlineData("tv", BaseItemKind.Series)]
  public void MediaTypeToKind_MapsKnownTypes(string mediaType, BaseItemKind expected)
  {
    Assert.Equal(expected, LibraryMatcher.MediaTypeToKind(mediaType));
  }

  [Fact]
  public void MediaTypeToKind_UnknownReturnsNull()
  {
    Assert.Null(LibraryMatcher.MediaTypeToKind("book"));
  }

  [Fact]
  public void GetSizeBytes_Movie_ReturnsItemSize()
  {
    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
      .Returns(new List<BaseItem> { new Movie { Size = 1000 } });

    var matcher = new LibraryMatcher(manager.Object);

    Assert.Equal(1000, matcher.GetSizeBytes("movie", 1));
  }

  [Fact]
  public void ListLibraryMedia_ListsShowsPerSeason()
  {
    var series = new Series { Id = Guid.NewGuid(), Name = "For All Mankind" };
    series.SetProviderId(MetadataProvider.Tmdb, "555");

    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
      .Returns(new List<BaseItem>());
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Series))))
      .Returns(new List<BaseItem> { series });
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Season))))
      .Returns(new List<BaseItem> { new Season { Id = Guid.NewGuid(), IndexNumber = 1 }, new Season { Id = Guid.NewGuid(), IndexNumber = 2 } });
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Episode))))
      .Returns(new List<BaseItem> { new Episode { Size = 100 } });

    var media = new LibraryMatcher(manager.Object).ListLibraryMedia();

    var tv = media.Where(m => m.MediaType == "tv").OrderBy(m => m.Season).ToList();
    Assert.Equal(2, tv.Count); // one entry per season, not one for the whole series
    Assert.Equal(new int?[] { 1, 2 }, tv.Select(m => m.Season).ToArray());
    Assert.All(tv, m => Assert.Equal(555, m.TmdbId));
    Assert.All(tv, m => Assert.Equal("For All Mankind", m.Title));
  }

  [Fact]
  public void ListLibraryMedia_FallsBackToWholeSeries_WhenNoSeasons()
  {
    var series = new Series { Id = Guid.NewGuid(), Name = "Flat Show" };
    series.SetProviderId(MetadataProvider.Tmdb, "42");

    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
      .Returns(new List<BaseItem>());
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Series))))
      .Returns(new List<BaseItem> { series });
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Season))))
      .Returns(new List<BaseItem>());
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Episode))))
      .Returns(new List<BaseItem>());

    var tv = new LibraryMatcher(manager.Object).ListLibraryMedia().Where(m => m.MediaType == "tv").ToList();

    Assert.Single(tv);
    Assert.Null(tv[0].Season);
  }

  [Fact]
  public void GetSizeBytes_Series_SumsEpisodeSizes()
  {
    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q =>
        q.IncludeItemTypes.Length > 0 && q.IncludeItemTypes[0] == BaseItemKind.Series)))
      .Returns(new List<BaseItem> { new Series() });
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q =>
        q.IncludeItemTypes.Length > 0 && q.IncludeItemTypes[0] == BaseItemKind.Episode)))
      .Returns(new List<BaseItem> { new Episode { Size = 500 }, new Episode { Size = 700 } });

    var matcher = new LibraryMatcher(manager.Object);

    Assert.Equal(1200, matcher.GetSizeBytes("tv", 5));
  }
}
