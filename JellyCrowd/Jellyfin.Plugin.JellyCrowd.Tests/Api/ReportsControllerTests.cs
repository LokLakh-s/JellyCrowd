using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="ReportsController"/> using a real <see cref="JsonReportStore"/>.
/// </summary>
public sealed class ReportsControllerTests : IDisposable
{
  private static readonly Guid User = Guid.NewGuid();
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonReportStore _store;

  public ReportsControllerTests() => _store = new JsonReportStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private ReportsController CreateController()
    => new(_store, new FakeUserAccessor(), _ => "tester", Mock.Of<IActivityLog>(), Mock.Of<INotificationService>())
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

  [Fact]
  public async Task Post_CreatesReport()
  {
    var result = await CreateController().Post(new ReportDto { MediaType = "movie", TmdbId = 1, Title = "M", Message = " bad subs " }, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var report = Assert.IsType<MediaReport>(ok.Value);
    Assert.Equal("bad subs", report.Message);
    Assert.Equal("tester", report.UserName);
  }

  [Theory]
  [InlineData("subtitles", "subtitles")]
  [InlineData("AUDIO", "audio")]
  [InlineData("nonsense", "other")]
  [InlineData(null, "other")]
  public async Task Post_NormalizesType(string? given, string expected)
  {
    var result = await CreateController().Post(
      new ReportDto { MediaType = "movie", TmdbId = 1, Title = "M", Message = "x", Type = given! }, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal(expected, Assert.IsType<MediaReport>(ok.Value).Type);
  }

  [Fact]
  public async Task Post_EmptyMessage_BadRequest()
  {
    var result = await CreateController().Post(new ReportDto { MediaType = "movie", TmdbId = 1, Message = "  " }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task ResolveThenList_ReflectsState()
  {
    var created = await _store.AddAsync(new MediaReport { MediaType = "movie", TmdbId = 1, Title = "M", UserId = User, UserName = "u", Message = "x" }, CancellationToken.None);

    await CreateController().Resolve(created.Id, new ResolveReportDto { Response = "fixed" }, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>((await CreateController().GetAll(CancellationToken.None)).Result);
    var list = Assert.IsAssignableFrom<IReadOnlyList<MediaReport>>(ok.Value);
    Assert.True(Assert.Single(list).Resolved);
  }

  [Fact]
  public async Task Resolve_NotifiesReporter_WithNote()
  {
    var created = await _store.AddAsync(
      new MediaReport { MediaType = "movie", TmdbId = 1, Title = "M", UserId = User, UserName = "u", Message = "x" }, CancellationToken.None);
    var notifications = new Mock<INotificationService>();
    var controller = new ReportsController(_store, new FakeUserAccessor(), _ => "tester", Mock.Of<IActivityLog>(), notifications.Object)
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    var result = await controller.Resolve(created.Id, new ResolveReportDto { Response = "Fixed the subs." }, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal("Fixed the subs.", Assert.IsType<MediaReport>(ok.Value).AdminResponse);
    notifications.Verify(
      n => n.NotifyPersonalAsync(User, It.IsAny<PersonalNotifyKind>(), It.IsAny<string>(), It.IsAny<string>(),
        It.Is<string>(b => b.Contains("Fixed the subs.", StringComparison.Ordinal)), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
      Times.Once);
  }

  [Fact]
  public async Task Post_NoMediaType_FilesAGeneralReport()
  {
    // The avatar-menu entry point: a problem that is not about one title still has to reach an admin.
    var result = await CreateController().Post(new ReportDto { Message = "I can't sign in", Type = "account" }, CancellationToken.None);

    var report = Assert.IsType<MediaReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(string.Empty, report.MediaType);
    Assert.Equal(0, report.TmdbId);
    Assert.Equal(string.Empty, report.Title);
    Assert.Equal("account", report.Type);
  }

  [Fact]
  public async Task Post_GeneralReport_DropsAnyTitleSentAlongside()
  {
    // Without a media type there is nothing to point at, so a stray title must not be filed as if the
    // report were about that title.
    var result = await CreateController().Post(
      new ReportDto { Message = "playback stutters", TmdbId = 42, Title = "Dune" }, CancellationToken.None);

    var report = Assert.IsType<MediaReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(0, report.TmdbId);
    Assert.Equal(string.Empty, report.Title);
  }

  [Fact]
  public async Task Post_UnknownMediaType_BadRequest()
  {
    var result = await CreateController().Post(new ReportDto { MediaType = "book", TmdbId = 1, Message = "x" }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task Post_NotifiesAdministrators()
  {
    var notifications = new RecordingNotificationService();
    var controller = new ReportsController(_store, new FakeUserAccessor(), _ => "tester", Mock.Of<IActivityLog>(), notifications)
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    await controller.Post(new ReportDto { MediaType = "movie", TmdbId = 1, Title = "Dune", Message = "no sound" }, CancellationToken.None);

    var notice = Assert.Single(notifications.AdminNotices);
    Assert.Contains("no sound", notice.Body, StringComparison.Ordinal);
    Assert.Contains("tester", notice.Body, StringComparison.Ordinal);
  }

  private const string EpisodeItemId = "0123456789abcdef0123456789abcdef";

  private async Task<MediaReport> PostOk(ReportDto dto)
  {
    var result = await CreateController().Post(dto, CancellationToken.None);
    return Assert.IsType<MediaReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
  }

  [Fact]
  public async Task Post_FromAnEpisodePage_KeepsTheSeasonTheEpisodeAndTheItem()
  {
    // The native detail page's button (issue #20): the series, narrowed to the episode it was filed from,
    // and the item itself for the administrator's link.
    var report = await PostOk(new ReportDto
    {
      MediaType = "tv", TmdbId = 70523, Title = "Dark", Season = 2, Episode = 5, ItemId = EpisodeItemId, Message = "no sound", Type = "audio"
    });

    Assert.Equal("tv", report.MediaType);
    Assert.Equal(70523, report.TmdbId);
    Assert.Equal(2, report.Season);
    Assert.Equal(5, report.Episode);
    Assert.Equal(EpisodeItemId, report.ItemId);
  }

  [Fact]
  public async Task Post_FromASeasonPage_KeepsTheSeasonOnly()
  {
    var report = await PostOk(new ReportDto { MediaType = "tv", TmdbId = 70523, Title = "Dark", Season = 0, Message = "x" });

    Assert.Equal(0, report.Season);
    Assert.Null(report.Episode);
  }

  [Fact]
  public async Task Post_TitleJellyfinNeverMatchedOnTmdb_IsStillFiled()
  {
    // A home video or a show identified only by TVDB has no TMDB id: the report goes out under its name.
    var report = await PostOk(new ReportDto { MediaType = "movie", TmdbId = 0, Title = "Holidays 2019", ItemId = EpisodeItemId, Message = "x" });

    Assert.Equal("movie", report.MediaType);
    Assert.Equal(0, report.TmdbId);
    Assert.Equal("Holidays 2019", report.Title);
    Assert.Equal(EpisodeItemId, report.ItemId);
  }

  [Fact]
  public async Task Post_ItemId_IsStoredInJellyfinsCompactForm()
  {
    var report = await PostOk(new ReportDto { MediaType = "tv", TmdbId = 1, Title = "T", ItemId = "01234567-89AB-CDEF-0123-456789ABCDEF", Message = "x" });

    Assert.Equal(EpisodeItemId, report.ItemId);
  }

  [Theory]
  [InlineData("javascript:alert(1)")]
  [InlineData("00000000000000000000000000000000")]
  [InlineData("")]
  public async Task Post_ItemIdThatIsNotAJellyfinId_IsDropped(string itemId)
  {
    // It becomes a link in the admin queue: anything but a real id is left out, the report still filed.
    var report = await PostOk(new ReportDto { MediaType = "tv", TmdbId = 1, Title = "T", ItemId = itemId, Message = "x" });

    Assert.Equal(string.Empty, report.ItemId);
  }

  [Fact]
  public async Task Post_Movie_DropsAnySeasonOrEpisode()
  {
    var report = await PostOk(new ReportDto { MediaType = "movie", TmdbId = 1, Title = "Dune", Season = 1, Episode = 2, Message = "x" });

    Assert.Null(report.Season);
    Assert.Null(report.Episode);
  }

  [Theory]
  [InlineData(null, 5)]
  [InlineData(-1, 5)]
  public async Task Post_EpisodeWithoutAValidSeason_IsDropped(int? season, int? episode)
  {
    var report = await PostOk(new ReportDto { MediaType = "tv", TmdbId = 1, Title = "Dark", Season = season, Episode = episode, Message = "x" });

    Assert.Null(report.Season);
    Assert.Null(report.Episode);
    Assert.Equal("Dark", report.Title);
  }

  [Fact]
  public async Task Post_NegativeEpisode_KeepsTheSeason()
  {
    var report = await PostOk(new ReportDto { MediaType = "tv", TmdbId = 1, Title = "Dark", Season = 2, Episode = -3, Message = "x" });

    Assert.Equal(2, report.Season);
    Assert.Null(report.Episode);
  }

  [Fact]
  public async Task Post_GeneralReport_DropsAnySeasonEpisodeOrItem()
  {
    var report = await PostOk(new ReportDto { Season = 2, Episode = 5, ItemId = EpisodeItemId, Message = "playback stutters" });

    Assert.Null(report.Season);
    Assert.Null(report.Episode);
    Assert.Equal(string.Empty, report.ItemId);
  }

  [Fact]
  public async Task Post_FromAnEpisode_TellsTheAdministratorsWhichOne()
  {
    var notifications = new RecordingNotificationService();
    var controller = new ReportsController(_store, new FakeUserAccessor(), _ => "tester", Mock.Of<IActivityLog>(), notifications)
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    await controller.Post(new ReportDto { MediaType = "tv", TmdbId = 70523, Title = "Dark", Season = 2, Episode = 5, Message = "no sound" }, CancellationToken.None);

    Assert.Contains("Dark · S2E5", Assert.Single(notifications.AdminNotices).Body, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Resolve_TellsTheReporterWhichEpisode()
  {
    var created = await _store.AddAsync(
      new MediaReport { MediaType = "tv", TmdbId = 70523, Title = "Dark", Season = 2, Episode = 5, UserId = User, UserName = "u", Message = "x" },
      CancellationToken.None);
    var notifications = new Mock<INotificationService>();
    var controller = new ReportsController(_store, new FakeUserAccessor(), _ => "tester", Mock.Of<IActivityLog>(), notifications.Object)
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    await controller.Resolve(created.Id, null, CancellationToken.None);

    notifications.Verify(
      n => n.NotifyPersonalAsync(User, It.IsAny<PersonalNotifyKind>(), "Dark · S2E5", It.IsAny<string>(),
        It.Is<string>(b => b.Contains("Dark · S2E5", StringComparison.Ordinal)), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
      Times.Once);
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(false);
  }
}
