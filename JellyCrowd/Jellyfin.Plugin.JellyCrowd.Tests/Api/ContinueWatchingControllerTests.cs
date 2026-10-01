using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities.Movies;
using JfEpisode = MediaBrowser.Controller.Entities.TV.Episode;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="ContinueWatchingController"/>.
/// </summary>
public class ContinueWatchingControllerTests
{
  private static readonly Guid Viewer = Guid.NewGuid();

  private sealed record Fixture(ContinueWatchingController Controller, List<HiddenResumeEntry> Saved, Mock<IHiddenResumeStore> Store, Mock<ILibraryManager> Library);

  private static Fixture Create()
  {
    var saved = new List<HiddenResumeEntry>();
    var store = new Mock<IHiddenResumeStore>();
    store.Setup(s => s.HideAsync(It.IsAny<HiddenResumeEntry>(), It.IsAny<CancellationToken>()))
      .Callback<HiddenResumeEntry, CancellationToken>((e, _) => saved.Add(e))
      .Returns(Task.CompletedTask);
    store.Setup(s => s.GetByUserAsync(Viewer, It.IsAny<CancellationToken>())).ReturnsAsync(() => saved);
    var library = new Mock<ILibraryManager>();
    var accessor = Mock.Of<ICurrentUserAccessor>(a => a.GetUserIdAsync(It.IsAny<HttpRequest>()) == Task.FromResult(Viewer));
    var controller = new ContinueWatchingController(store.Object, library.Object, accessor)
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
    return new Fixture(controller, saved, store, library);
  }

  [Fact]
  public async Task Hide_Movie_RecordsTheMovie()
  {
    var (controller, saved, _, library) = Create();
    var movie = new Movie { Id = Guid.NewGuid(), Name = "Dune" };
    library.Setup(l => l.GetItemById(movie.Id)).Returns(movie);

    Assert.IsType<NoContentResult>(await controller.Hide(movie.Id, CancellationToken.None));

    var entry = Assert.Single(saved);
    Assert.Equal(Viewer, entry.UserId);
    Assert.Equal(movie.Id, entry.ItemId);
    Assert.Null(entry.SeriesId);
    Assert.Equal("Dune", entry.Title);
  }

  [Fact]
  public async Task Hide_Episode_RecordsItsShow()
  {
    var (controller, saved, _, library) = Create();
    var show = Guid.NewGuid();
    var episode = new JfEpisode { Id = Guid.NewGuid(), SeriesId = show, SeriesName = "Severance", Name = "Hello, Ms. Cobel" };
    library.Setup(l => l.GetItemById(episode.Id)).Returns(episode);

    Assert.IsType<NoContentResult>(await controller.Hide(episode.Id, CancellationToken.None));

    var entry = Assert.Single(saved);
    Assert.Equal(show, entry.SeriesId);
    Assert.Equal("Severance", entry.Title);
  }

  [Fact]
  public async Task Hide_UnknownItem_Returns404()
  {
    var (controller, saved, _, _) = Create();

    Assert.IsType<NotFoundResult>(await controller.Hide(Guid.NewGuid(), CancellationToken.None));
    Assert.Empty(saved);
  }

  [Fact]
  public async Task Unhide_ReportsWhetherSomethingWasPutBack()
  {
    var (controller, _, store, _) = Create();
    var known = Guid.NewGuid();
    store.Setup(s => s.UnhideAsync(Viewer, known, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    Assert.IsType<NoContentResult>(await controller.Unhide(known, CancellationToken.None));
    Assert.IsType<NotFoundResult>(await controller.Unhide(Guid.NewGuid(), CancellationToken.None));
  }

  [Fact]
  public async Task Hidden_ListsTheCallersRemovals()
  {
    var (controller, saved, _, _) = Create();
    saved.Add(new HiddenResumeEntry { UserId = Viewer, ItemId = Guid.NewGuid(), Title = "Dune" });

    var ok = Assert.IsType<OkObjectResult>((await controller.Hidden(CancellationToken.None)).Result);

    Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<HiddenResumeEntry>>(ok.Value));
  }
}
