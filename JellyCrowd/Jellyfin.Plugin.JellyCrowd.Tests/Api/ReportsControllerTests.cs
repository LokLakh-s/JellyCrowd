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
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="ReportsController"/> using a real <see cref="JsonReportStore"/>.
/// </summary>
public sealed class ReportsControllerTests : IDisposable
{
  private static readonly Guid User = Guid.NewGuid();
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
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
    => new(_store, new FakeUserAccessor(), _ => "tester")
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

    await CreateController().Resolve(created.Id, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>((await CreateController().GetAll(CancellationToken.None)).Result);
    var list = Assert.IsAssignableFrom<IReadOnlyList<MediaReport>>(ok.Value);
    Assert.True(Assert.Single(list).Resolved);
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(false);
  }
}
