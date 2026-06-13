using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
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
