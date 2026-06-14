using System;
using System.Collections.Generic;
using System.Linq;
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
/// Tests for <see cref="RequestsController"/>.
/// </summary>
public class RequestsControllerTests
{
  private static readonly Guid User = Guid.NewGuid();

  private static RequestsController CreateController(IRequestStore store, Guid? userId = null, bool canRequest = true)
  {
    var controller = new RequestsController(store, new FakeUserAccessor(userId ?? User), new FakeQuotaService(canRequest), new FakeNotificationService(), new FakeDownloadDispatcher())
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    return controller;
  }

  private static CreateRequestDto ValidDto()
    => new() { TmdbId = 100, MediaType = "movie", Title = "Dune" };

  [Fact]
  public async Task Create_Valid_ReturnsOkPendingForCurrentUser()
  {
    var controller = CreateController(new FakeRequestStore());

    var result = await controller.Create(ValidDto(), CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var record = Assert.IsType<RequestRecord>(ok.Value);
    Assert.Equal(User, record.UserId);
    Assert.Equal(RequestStatus.Pending, record.Status);
  }

  [Fact]
  public async Task Create_StoresProvidedDesiredDate()
  {
    var controller = CreateController(new FakeRequestStore());
    var desired = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    var result = await controller.Create(
      new CreateRequestDto { TmdbId = 1, MediaType = "movie", Title = "Dune", DesiredAt = desired },
      CancellationToken.None);

    var record = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(desired, record.DesiredAt);
  }

  [Fact]
  public async Task Create_DefaultsDesiredDateToNow_WhenOmitted()
  {
    var controller = CreateController(new FakeRequestStore());
    var before = DateTime.UtcNow;

    var result = await controller.Create(ValidDto(), CancellationToken.None);

    var record = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.NotNull(record.DesiredAt);
    Assert.InRange(record.DesiredAt!.Value, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
  }

  [Fact]
  public async Task Create_FutureRelease_SchedulesDesiredAtRelease()
  {
    var controller = CreateController(new FakeRequestStore());

    var result = await controller.Create(
      new CreateRequestDto { TmdbId = 1, MediaType = "movie", Title = "Future", ReleaseDate = "2030-01-15" },
      CancellationToken.None);

    var record = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(new DateTime(2030, 1, 15, 0, 0, 0, DateTimeKind.Utc), record.DesiredAt);
  }

  [Fact]
  public async Task Create_Episode_StoresEpisodeAndAllowsOtherEpisodes()
  {
    var store = new FakeRequestStore();
    var ep3 = new CreateRequestDto { TmdbId = 7, MediaType = "tv", Title = "Show", Season = 2, Episode = 3 };

    var created = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>((await CreateController(store).Create(ep3, CancellationToken.None)).Result).Value);
    Assert.Equal(3, created.Episode);

    // Same episode again -> conflict.
    Assert.IsType<ConflictObjectResult>((await CreateController(store).Create(ep3, CancellationToken.None)).Result);

    // A different episode of the same season is allowed.
    var ep4 = new CreateRequestDto { TmdbId = 7, MediaType = "tv", Title = "Show", Season = 2, Episode = 4 };
    Assert.IsType<OkObjectResult>((await CreateController(store).Create(ep4, CancellationToken.None)).Result);
  }

  [Fact]
  public async Task Create_InvalidMediaType_ReturnsBadRequest()
  {
    var controller = CreateController(new FakeRequestStore());

    var result = await controller.Create(new CreateRequestDto { TmdbId = 1, MediaType = "book", Title = "X" }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task Create_MissingTitle_ReturnsBadRequest()
  {
    var controller = CreateController(new FakeRequestStore());

    var result = await controller.Create(new CreateRequestDto { TmdbId = 1, MediaType = "movie", Title = "  " }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task Create_Duplicate_ReturnsConflict()
  {
    var store = new FakeRequestStore();
    var controller = CreateController(store);
    await controller.Create(ValidDto(), CancellationToken.None);

    var result = await controller.Create(ValidDto(), CancellationToken.None);

    Assert.IsType<ConflictObjectResult>(result.Result);
  }

  [Fact]
  public async Task Create_OverQuota_ReturnsForbidden()
  {
    var result = await CreateController(new FakeRequestStore(), canRequest: false).Create(ValidDto(), CancellationToken.None);

    var obj = Assert.IsType<ObjectResult>(result.Result);
    Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
  }

  [Fact]
  public async Task Mine_ReturnsOnlyCurrentUserRequests()
  {
    var store = new FakeRequestStore();
    await CreateController(store, User).Create(ValidDto(), CancellationToken.None);
    await CreateController(store, Guid.NewGuid()).Create(new CreateRequestDto { TmdbId = 7, MediaType = "tv", Title = "Y" }, CancellationToken.None);

    var result = await CreateController(store, User).Mine(CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var items = Assert.IsAssignableFrom<IReadOnlyList<RequestRecord>>(ok.Value);
    Assert.Single(items);
    Assert.Equal(User, items[0].UserId);
  }

  [Fact]
  public async Task Approve_Existing_ReturnsApproved()
  {
    var store = new FakeRequestStore();
    var create = (OkObjectResult)(await CreateController(store).Create(ValidDto(), CancellationToken.None)).Result!;
    var id = ((RequestRecord)create.Value!).Id;

    var result = await CreateController(store).Approve(id, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal(RequestStatus.Approved, ((RequestRecord)ok.Value!).Status);
  }

  [Fact]
  public async Task Deny_Missing_ReturnsNotFound()
  {
    var result = await CreateController(new FakeRequestStore()).Deny(Guid.NewGuid(), CancellationToken.None);

    Assert.IsType<NotFoundResult>(result.Result);
  }

  [Fact]
  public async Task Cancel_OwnPending_ReturnsNoContent()
  {
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store).Create(ValidDto(), CancellationToken.None)).Result!).Value!;

    var result = await CreateController(store).Cancel(created.Id, CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
  }

  [Fact]
  public async Task Cancel_Approved_ReturnsNotFound()
  {
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store).Create(ValidDto(), CancellationToken.None)).Result!).Value!;
    await store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);

    var result = await CreateController(store).Cancel(created.Id, CancellationToken.None);

    Assert.IsType<NotFoundResult>(result);
  }

  [Fact]
  public async Task RequestDeletion_OwnAvailable_ReturnsOk()
  {
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store).Create(ValidDto(), CancellationToken.None)).Result!).Value!;
    await store.MarkAvailableAsync(created.Id, "x", CancellationToken.None);

    var result = await CreateController(store).RequestDeletion(created.Id, CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
  }

  [Fact]
  public async Task RequestDeletion_NotOwner_ReturnsNotFound()
  {
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store, User).Create(ValidDto(), CancellationToken.None)).Result!).Value!;
    await store.MarkAvailableAsync(created.Id, "x", CancellationToken.None);

    var result = await CreateController(store, Guid.NewGuid()).RequestDeletion(created.Id, CancellationToken.None);

    Assert.IsType<NotFoundResult>(result.Result);
  }

  [Fact]
  public async Task Delete_Existing_ReturnsNoContent_AndRemoves()
  {
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store).Create(ValidDto(), CancellationToken.None)).Result!).Value!;

    var result = await CreateController(store).Delete(created.Id, CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
    Assert.Empty(await store.GetAllAsync(CancellationToken.None));
  }

  [Fact]
  public async Task Delete_Missing_ReturnsNotFound()
  {
    Assert.IsType<NotFoundResult>(await CreateController(new FakeRequestStore()).Delete(Guid.NewGuid(), CancellationToken.None));
  }

  [Fact]
  public async Task Edit_Existing_UpdatesStatusSeasonAndDesired()
  {
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store).Create(ValidDto(), CancellationToken.None)).Result!).Value!;
    var desired = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    var result = await CreateController(store).Edit(
      created.Id,
      new AdminEditRequestDto { Status = RequestStatus.Available, Season = 3, Episode = 5, DesiredAt = desired },
      CancellationToken.None);

    var record = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(RequestStatus.Available, record.Status);
    Assert.Equal(3, record.Season);
    Assert.Equal(5, record.Episode);
    Assert.Equal(desired, record.DesiredAt);
  }

  [Fact]
  public async Task Edit_Missing_ReturnsNotFound()
  {
    var result = await CreateController(new FakeRequestStore()).Edit(Guid.NewGuid(), new AdminEditRequestDto(), CancellationToken.None);
    Assert.IsType<NotFoundResult>(result.Result);
  }

  [Fact]
  public async Task CreateForUser_CreatesForTargetUser()
  {
    var store = new FakeRequestStore();
    var target = Guid.NewGuid();

    var result = await CreateController(store).CreateForUser(
      new AdminCreateRequestDto { UserId = target, TmdbId = 5, MediaType = "movie", Title = "Dune" },
      CancellationToken.None);

    var record = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(target, record.UserId);
    Assert.Equal(RequestStatus.Approved, record.Status);
  }

  [Fact]
  public async Task CreateForUser_MissingUser_ReturnsBadRequest()
  {
    var result = await CreateController(new FakeRequestStore()).CreateForUser(
      new AdminCreateRequestDto { UserId = Guid.Empty, TmdbId = 5, MediaType = "movie", Title = "Dune" },
      CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    private readonly Guid _userId;

    public FakeUserAccessor(Guid userId) => _userId = userId;

    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(_userId);
  }

  private sealed class FakeQuotaService : IQuotaService
  {
    private readonly bool _canRequest;

    public FakeQuotaService(bool canRequest) => _canRequest = canRequest;

    public long GetQuotaBytes(Guid userId) => 0;

    public Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken)
      => Task.FromResult(new QuotaInfo());

    public Task<bool> CanRequestAsync(Guid userId, string mediaType, CancellationToken cancellationToken)
      => Task.FromResult(_canRequest);
  }

  private sealed class FakeNotificationService : INotificationService
  {
    public Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task SendTestAsync(string channel, CancellationToken cancellationToken) => Task.CompletedTask;
  }

  private sealed class FakeDownloadDispatcher : IDownloadDispatcher
  {
    public Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task DispatchDueAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TestActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
  }

  private sealed class FakeRequestStore : IRequestStore
  {
    private readonly List<RequestRecord> _items = new();

    public Task<RequestRecord> CreateAsync(RequestRecord record, CancellationToken cancellationToken)
    {
      record.Id = Guid.NewGuid();
      record.RequestedAt = DateTime.UtcNow;
      _items.Add(record);
      return Task.FromResult(record);
    }

    public Task<IReadOnlyList<RequestRecord>> GetAllAsync(CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<RequestRecord>>(_items.ToList());

    public Task<IReadOnlyList<RequestRecord>> GetByUserAsync(Guid userId, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<RequestRecord>>(_items.Where(r => r.UserId == userId).ToList());

    public Task<RequestRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
      => Task.FromResult(_items.FirstOrDefault(r => r.Id == id));

    public Task<RequestRecord?> UpdateStatusAsync(Guid id, RequestStatus status, Guid decidedBy, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is not null)
      {
        record.Status = status;
        record.DecidedBy = decidedBy;
        record.DecidedAt = DateTime.UtcNow;
      }

      return Task.FromResult(record);
    }

    public Task<bool> ExistsActiveAsync(Guid userId, int tmdbId, string mediaType, int? season, int? episode, CancellationToken cancellationToken)
      => Task.FromResult(_items.Any(r =>
        r.UserId == userId && r.TmdbId == tmdbId
        && string.Equals(r.MediaType, mediaType, StringComparison.Ordinal)
        && r.Season == season
        && r.Episode == episode
        && r.Status != RequestStatus.Denied));

    public Task<int> CountUserRequestsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken)
      => Task.FromResult(_items.Count(r => r.UserId == userId && r.Status != RequestStatus.Denied && r.RequestedAt >= sinceUtc));

    public Task<RequestRecord?> MarkAvailableAsync(Guid id, string jellyfinItemId, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is not null)
      {
        record.Status = RequestStatus.Available;
        record.JellyfinItemId = jellyfinItemId;
      }

      return Task.FromResult(record);
    }

    public Task<bool> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.UserId != userId || record.Status != RequestStatus.Pending)
      {
        return Task.FromResult(false);
      }

      _items.Remove(record);
      return Task.FromResult(true);
    }

    public Task<RequestRecord?> RequestDeletionAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.UserId != userId || record.Status != RequestStatus.Available || record.DeletionRequestedAt is not null)
      {
        return Task.FromResult<RequestRecord?>(null);
      }

      record.DeletionRequestedAt = DateTime.UtcNow;
      return Task.FromResult<RequestRecord?>(record);
    }

    public Task<bool> AnyActiveReferenceAsync(Guid excludeId, int tmdbId, string mediaType, CancellationToken cancellationToken)
      => Task.FromResult(_items.Any(r => r.Id != excludeId && r.TmdbId == tmdbId
        && string.Equals(r.MediaType, mediaType, StringComparison.Ordinal)
        && r.DeletionRequestedAt is null && r.Status != RequestStatus.Denied));

    public Task<IReadOnlyList<RequestRecord>> GetDueForDeletionAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<RequestRecord>>(_items.Where(r => r.DeletionRequestedAt is not null && r.DeletionRequestedAt <= cutoffUtc).ToList());

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
      _items.RemoveAll(r => r.Id == id);
      return Task.CompletedTask;
    }

    public Task<RequestRecord?> MarkDispatchedAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is not null)
      {
        record.DispatchedAt = whenUtc;
      }

      return Task.FromResult(record);
    }

    public Task<IReadOnlyList<RequestRecord>> GetDueForDispatchAsync(DateTime nowUtc, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<RequestRecord>>(_items.Where(r =>
        r.Status == RequestStatus.Approved && r.DispatchedAt is null
        && (r.DesiredAt is null || r.DesiredAt <= nowUtc)).ToList());

    public Task<RequestRecord?> AdminUpdateAsync(Guid id, RequestStatus status, int? season, int? episode, DateTime? desiredAt, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is not null)
      {
        record.Status = status;
        record.Season = season;
        record.Episode = episode;
        record.DesiredAt = desiredAt;
      }

      return Task.FromResult(record);
    }
  }
}
