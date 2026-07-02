using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="EmptyLibraryCleaner"/> against a mocked <see cref="ILibraryManager"/>.
/// </summary>
public class EmptyLibraryCleanerTests
{
  private static Series Series(Guid id, DateTime created, int? tmdbId = null)
  {
    var series = new Series { Id = id, DateCreated = created };
    if (tmdbId is int t)
    {
      series.SetProviderId(MetadataProvider.Tmdb, t.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    return series;
  }

  private static Mock<ILibraryManager> ManagerWith(IReadOnlyList<BaseItem> series, IReadOnlyDictionary<Guid, bool> hasEpisode)
  {
    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Series))))
      .Returns(series.ToList());
    manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Episode))))
      .Returns((InternalItemsQuery q) =>
        q.AncestorIds.Length > 0 && hasEpisode.TryGetValue(q.AncestorIds[0], out var has) && has
          ? new List<BaseItem> { new Episode() }
          : new List<BaseItem>());
    return manager;
  }

  [Fact]
  public void RemovesEmptyOldSeries_ButKeepsFull_Recent_AndWanted()
  {
    var now = DateTime.UtcNow;
    var emptyOld = Guid.NewGuid();
    var full = Guid.NewGuid();
    var emptyRecent = Guid.NewGuid();
    var emptyWanted = Guid.NewGuid();

    var series = new List<BaseItem>
    {
      Series(emptyOld, now.AddDays(-5), tmdbId: 1),
      Series(full, now.AddDays(-5), tmdbId: 2),
      Series(emptyRecent, now.AddHours(-1), tmdbId: 3),
      Series(emptyWanted, now.AddDays(-5), tmdbId: 99)
    };
    var hasEpisode = new Dictionary<Guid, bool> { [emptyOld] = false, [full] = true, [emptyRecent] = false, [emptyWanted] = false };

    var manager = ManagerWith(series, hasEpisode);
    var cleaner = new EmptyLibraryCleaner(manager.Object, NullLogger<EmptyLibraryCleaner>.Instance);

    var removed = cleaner.RemoveEmptySeries(24, new HashSet<int> { 99 });

    Assert.Equal(1, removed);
    manager.Verify(m => m.DeleteItem(It.Is<BaseItem>(b => b.Id == emptyOld), It.IsAny<DeleteOptions>()), Times.Once);
    manager.Verify(m => m.DeleteItem(It.Is<BaseItem>(b => b.Id == full), It.IsAny<DeleteOptions>()), Times.Never);
    manager.Verify(m => m.DeleteItem(It.Is<BaseItem>(b => b.Id == emptyRecent), It.IsAny<DeleteOptions>()), Times.Never);
    manager.Verify(m => m.DeleteItem(It.Is<BaseItem>(b => b.Id == emptyWanted), It.IsAny<DeleteOptions>()), Times.Never);
  }

  [Fact]
  public void DeletesFileLocation_SoTheEmptyFolderIsRemoved()
  {
    var id = Guid.NewGuid();
    var manager = ManagerWith(
      new List<BaseItem> { Series(id, DateTime.UtcNow.AddDays(-3)) },
      new Dictionary<Guid, bool> { [id] = false });

    new EmptyLibraryCleaner(manager.Object, NullLogger<EmptyLibraryCleaner>.Instance).RemoveEmptySeries(24, new HashSet<int>());

    manager.Verify(m => m.DeleteItem(It.IsAny<BaseItem>(), It.Is<DeleteOptions>(o => o.DeleteFileLocation)), Times.Once);
  }

  [Fact]
  public void ContinuesSweep_WhenOneDeletionThrows()
  {
    var throws = Guid.NewGuid();
    var ok = Guid.NewGuid();
    var manager = ManagerWith(
      new List<BaseItem> { Series(throws, DateTime.UtcNow.AddDays(-3)), Series(ok, DateTime.UtcNow.AddDays(-3)) },
      new Dictionary<Guid, bool> { [throws] = false, [ok] = false });
    manager.Setup(m => m.DeleteItem(It.Is<BaseItem>(b => b.Id == throws), It.IsAny<DeleteOptions>()))
      .Throws(new InvalidOperationException("locked"));

    var removed = new EmptyLibraryCleaner(manager.Object, NullLogger<EmptyLibraryCleaner>.Instance).RemoveEmptySeries(24, new HashSet<int>());

    Assert.Equal(1, removed); // the second one still got removed
    manager.Verify(m => m.DeleteItem(It.Is<BaseItem>(b => b.Id == ok), It.IsAny<DeleteOptions>()), Times.Once);
  }
}
