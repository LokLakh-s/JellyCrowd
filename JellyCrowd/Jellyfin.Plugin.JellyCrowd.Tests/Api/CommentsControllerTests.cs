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

  private CommentsController CreateController(bool commentsEnabled = true)
    => new(_store, new FakeUserAccessor(), _ => "tester", () => new Jellyfin.Plugin.JellyCrowd.Configuration.PluginConfiguration { CommentsEnabled = commentsEnabled })
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

  [Fact]
  public async Task Post_CreatesReviewWithRatingAndResolvedName()
  {
    var result = await CreateController().Post(new CommentDto { MediaType = "movie", TmdbId = 1, Title = "  Dune  ", Text = "  hello  ", Rating = 8 }, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var review = Assert.IsType<MediaComment>(ok.Value);
    Assert.Equal("hello", review.Text);
    Assert.Equal("Dune", review.Title); // captured + trimmed at post time
    Assert.Equal(8, review.Rating);
    Assert.Equal("tester", review.UserName);
    Assert.Equal(User, review.UserId);
  }

  [Fact]
  public async Task Post_NoRating_BadRequest()
  {
    // Text is optional now, but a 1–10 rating is required.
    var result = await CreateController().Post(new CommentDto { MediaType = "movie", TmdbId = 1, Text = "great", Rating = 0 }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task Post_SecondReview_UpdatesInsteadOfDuplicating()
  {
    await CreateController().Post(new CommentDto { MediaType = "movie", TmdbId = 7, Rating = 6 }, CancellationToken.None);
    await CreateController().Post(new CommentDto { MediaType = "movie", TmdbId = 7, Rating = 9 }, CancellationToken.None);

    var dto = Assert.IsType<MediaReviewsDto>(Assert.IsType<OkObjectResult>((await CreateController().Get("movie", 7, CancellationToken.None)).Result).Value);
    Assert.Equal(1, dto.Count);
    Assert.Equal(9, dto.Average);
  }

  [Fact]
  public async Task Get_ReturnsAverageAndAnonymisesForNonAdmin()
  {
    await CreateController().Post(new CommentDto { MediaType = "movie", TmdbId = 7, Text = "good", Rating = 8 }, CancellationToken.None);

    var dto = Assert.IsType<MediaReviewsDto>(Assert.IsType<OkObjectResult>((await CreateController().Get("movie", 7, CancellationToken.None)).Result).Value);

    Assert.Equal(1, dto.Count);
    Assert.Equal(8, dto.Average);
    var review = Assert.Single(dto.Reviews);
    Assert.Null(review.UserName); // anonymous to non-admins
    Assert.True(review.Mine);
  }

  [Fact]
  public async Task DeleteMine_OnlyOwnComment()
  {
    var other = await _store.AddAsync(new MediaComment { MediaType = "movie", TmdbId = 1, UserId = Guid.NewGuid(), UserName = "o", Text = "x" }, CancellationToken.None);

    var result = await CreateController().DeleteMine(other.Id, CancellationToken.None);

    Assert.IsType<NotFoundResult>(result); // not the caller's comment
  }

  [Fact]
  public async Task GetAll_ReturnsEveryReviewWithHiddenFlagAndAuthor()
  {
    var a = await _store.AddAsync(new MediaComment { MediaType = "movie", TmdbId = 1, Title = "Heat", UserId = Guid.NewGuid(), UserName = "alice", Text = "x", Rating = 7 }, CancellationToken.None);
    await _store.AddAsync(new MediaComment { MediaType = "tv", TmdbId = 2, UserId = Guid.NewGuid(), UserName = "bob", Text = "y", Rating = 3 }, CancellationToken.None);
    await CreateController().Hide(a.Id, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>((await CreateController().GetAll(CancellationToken.None)).Result);
    var all = Assert.IsAssignableFrom<IReadOnlyList<ModeratedReviewDto>>(ok.Value);

    Assert.Equal(2, all.Count);
    var hidden = Assert.Single(all, r => r.TmdbId == 1);
    Assert.True(hidden.Hidden);
    Assert.Equal("Heat", hidden.Title); // title captured at post time, surfaced for moderation
    Assert.Equal("alice", hidden.UserName); // admins always see authors
  }

  [Fact]
  public async Task Show_UnhidesAHiddenReview()
  {
    var a = await _store.AddAsync(new MediaComment { MediaType = "movie", TmdbId = 5, UserId = Guid.NewGuid(), UserName = "a", Text = "x", Rating = 5 }, CancellationToken.None);
    await CreateController().Hide(a.Id, CancellationToken.None);

    var result = await CreateController().Show(a.Id, CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
    var refreshed = await _store.GetByIdAsync(a.Id, CancellationToken.None);
    Assert.False(refreshed!.Hidden);
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(User);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(false);
  }
}
