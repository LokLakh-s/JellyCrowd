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
/// Tests for <see cref="CommentsController"/> using a real <see cref="JsonMediaCommentStore"/>.
/// </summary>
public sealed class CommentsControllerTests : IDisposable
{
  private static readonly Guid User = Guid.NewGuid();
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonMediaCommentStore _store;

  public CommentsControllerTests() => _store = new JsonMediaCommentStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private CommentsController CreateController()
    => new(_store, new FakeUserAccessor(), _ => "tester")
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

  [Fact]
  public async Task Post_CreatesCommentWithResolvedName()
  {
    var result = await CreateController().Post(new CommentDto { MediaType = "movie", TmdbId = 1, Text = "  hello  " }, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var comment = Assert.IsType<MediaComment>(ok.Value);
    Assert.Equal("hello", comment.Text);
    Assert.Equal("tester", comment.UserName);
    Assert.Equal(User, comment.UserId);
  }

  [Fact]
  public async Task Post_EmptyText_BadRequest()
  {
    var result = await CreateController().Post(new CommentDto { MediaType = "movie", TmdbId = 1, Text = "   " }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task Get_ReturnsVisibleComments()
  {
    await CreateController().Post(new CommentDto { MediaType = "movie", TmdbId = 7, Text = "x" }, CancellationToken.None);

    var result = await CreateController().Get("movie", 7, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MediaComment>>(ok.Value));
  }

  [Fact]
  public async Task DeleteMine_OnlyOwnComment()
  {
    var other = await _store.AddAsync(new MediaComment { MediaType = "movie", TmdbId = 1, UserId = Guid.NewGuid(), UserName = "o", Text = "x" }, CancellationToken.None);

    var result = await CreateController().DeleteMine(other.Id, CancellationToken.None);

    Assert.IsType<NotFoundResult>(result); // not the caller's comment
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);
  }
}
