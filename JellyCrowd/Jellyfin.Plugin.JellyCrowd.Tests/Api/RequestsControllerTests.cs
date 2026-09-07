using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Jellyfin.Plugin.JellyCrowd.Tests.Services;
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

  private static RequestsController CreateController(IRequestStore store, Guid? userId = null, bool canRequest = true, FakeDownloadDispatcher? dispatcher = null, ITmdbClient? tmdb = null, bool isAdmin = false, FakeQuotaService? quota = null, ILibraryMatcher? matcher = null, INotificationService? notifications = null)
  {
    var controller = new RequestsController(store, new FakeUserAccessor(userId ?? User, isAdmin), quota ?? new FakeQuotaService(canRequest), notifications ?? new FakeNotificationService(), dispatcher ?? new FakeDownloadDispatcher(), new FakeServarrStatusService(), matcher ?? new FakeLibraryMatcher(), tmdb ?? new StubTmdbClient(), new NoOpActivityLog(), _ => "tester")
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
  public async Task Create_FutureRelease_DoesNotConsultQuota()
  {
    // A not-yet-released title can't download yet, so its footprint must NOT be reserved and the
    // quota must NOT be gated at request time — it is re-checked when the title becomes due. Even
    // with the user over quota (canRequest: false), the request is accepted and never held for quota.
    var quota = new FakeQuotaService(canRequest: false);
    var controller = CreateController(new FakeRequestStore(), canRequest: false, quota: quota);

    var result = await controller.Create(
      new CreateRequestDto { TmdbId = 1, MediaType = "movie", Title = "Future", ReleaseDate = "2030-01-15" },
      CancellationToken.None);

    var record = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.False(record.HeldForQuota);
    Assert.Equal(0, quota.CanRequestCalls);
  }

  [Fact]
  public async Task Create_ReleasedTitle_ConsultsQuota()
  {
    // Contrast: a title that is downloadable now IS gated against the quota at request time.
    var quota = new FakeQuotaService(canRequest: true);
    var controller = CreateController(new FakeRequestStore(), quota: quota);

    await controller.Create(ValidDto(), CancellationToken.None);

    Assert.Equal(1, quota.CanRequestCalls);
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
  public async Task Ownerships_GroupsAvailableByTitleWithOwners()
  {
    var store = new FakeRequestStore();
    var u1 = Guid.NewGuid();
    var u2 = Guid.NewGuid();
    // Two users own the same movie; a pending request must NOT count; a TV season is its own group.
    await store.CreateAsync(new RequestRecord { UserId = u1, TmdbId = 1, MediaType = "movie", Title = "Dune", Status = RequestStatus.Available }, CancellationToken.None);
    await store.CreateAsync(new RequestRecord { UserId = u2, TmdbId = 1, MediaType = "movie", Title = "Dune", Status = RequestStatus.Available }, CancellationToken.None);
    await store.CreateAsync(new RequestRecord { UserId = u1, TmdbId = 1, MediaType = "movie", Title = "Dune", Status = RequestStatus.Pending }, CancellationToken.None);
    await store.CreateAsync(new RequestRecord { UserId = u2, TmdbId = 9, MediaType = "tv", Title = "Show", Season = 2, Status = RequestStatus.Available }, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>((await CreateController(store).Ownerships(CancellationToken.None)).Result);
    var list = Assert.IsAssignableFrom<IReadOnlyList<MediaOwnershipDto>>(ok.Value);

    Assert.Equal(2, list.Count);
    var movie = Assert.Single(list, d => d.TmdbId == 1);
    Assert.Equal(2, movie.Owners.Count); // two distinct owners, pending excluded
    Assert.Null(movie.Season);
    var season = Assert.Single(list, d => d.TmdbId == 9);
    Assert.Equal(2, season.Season);
    Assert.Single(season.Owners);
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
  public async Task Create_OverQuota_IsHeldAsPendingNotRejected()
  {
    var result = await CreateController(new FakeRequestStore(), canRequest: false).Create(ValidDto(), CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var created = Assert.IsType<RequestRecord>(ok.Value);
    Assert.Equal(RequestStatus.Pending, created.Status);
  }

  [Fact]
  public async Task Claim_AvailableTitle_CreatesAvailableOwnership()
  {
    var result = await CreateController(new FakeRequestStore()).Claim(ValidDto(), CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var created = Assert.IsType<RequestRecord>(ok.Value);
    Assert.Equal(RequestStatus.Available, created.Status);
    Assert.False(string.IsNullOrEmpty(created.JellyfinItemId));
  }

  [Fact]
  public async Task Claim_AlreadyOwned_RenewsInsteadOfDuplicating()
  {
    var store = new FakeRequestStore();
    var first = (RequestRecord)((OkObjectResult)(await CreateController(store).Claim(ValidDto(), CancellationToken.None)).Result!).Value!;

    var result = await CreateController(store).Claim(ValidDto(), CancellationToken.None);

    // Re-claiming renews the same ownership (resets the expiry countdown), not a duplicate.
    var renewed = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(first.Id, renewed.Id);
    Assert.Single(await store.GetByUserAsync(renewed.UserId, CancellationToken.None));
  }

  [Fact]
  public async Task Claim_Season_CreatesSeasonScopedOwnership()
  {
    var dto = new CreateRequestDto { TmdbId = 200, MediaType = "tv", Title = "Show", Season = 2 };

    var result = await CreateController(new FakeRequestStore()).Claim(dto, CancellationToken.None);

    var created = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(RequestStatus.Available, created.Status);
    Assert.Equal(2, created.Season);
    Assert.Equal("season-200", created.JellyfinItemId); // resolved via FindSeasonItemId, not the whole series
  }

  [Fact]
  public async Task Claim_Season_NotRenewedByADifferentSeason()
  {
    var store = new FakeRequestStore();
    await CreateController(store).Claim(new CreateRequestDto { TmdbId = 200, MediaType = "tv", Title = "Show", Season = 1 }, CancellationToken.None);

    // Claiming season 2 must create its own ownership, not renew season 1.
    var result = await CreateController(store).Claim(new CreateRequestDto { TmdbId = 200, MediaType = "tv", Title = "Show", Season = 2 }, CancellationToken.None);

    var created = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(2, created.Season);
    Assert.Equal(2, (await store.GetByUserAsync(created.UserId, CancellationToken.None)).Count);
  }

  [Fact]
  public async Task CancelDeletion_UnknownRequest_NotFound()
  {
    var result = await CreateController(new FakeRequestStore()).CancelDeletion(Guid.NewGuid(), CancellationToken.None);

    Assert.IsType<NotFoundResult>(result.Result);
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
  public async Task Retry_OwnApproved_ReturnsOk_AndCallsDispatcher()
  {
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store).Create(ValidDto(), CancellationToken.None)).Result!).Value!;
    await store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    var dispatcher = new FakeDownloadDispatcher();

    var result = await CreateController(store, User, dispatcher: dispatcher, isAdmin: true).Retry(created.Id, CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
    Assert.Contains(created.Id, dispatcher.Retried);
  }

  [Fact]
  public async Task Retry_NonAdmin_WhenNotAllowed_ReturnsForbidden()
  {
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store).Create(ValidDto(), CancellationToken.None)).Result!).Value!;
    await store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);

    // No Plugin.Instance in tests => AllowUserRetrySearch defaults false, and the caller is not admin.
    var result = await CreateController(store, User).Retry(created.Id, CancellationToken.None);

    Assert.IsType<ForbidResult>(result.Result);
  }

  [Fact]
  public async Task Retry_AdminCanRetryOtherUsersRequest()
  {
    // N25: an admin retries a request made by/for another user (e.g. on-behalf) — must succeed, not 404.
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store, User).Create(ValidDto(), CancellationToken.None)).Result!).Value!;
    await store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);

    var result = await CreateController(store, Guid.NewGuid(), isAdmin: true).Retry(created.Id, CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
  }

  [Fact]
  public async Task RetryAll_RetriesOnlyApprovedRequests()
  {
    var store = new FakeRequestStore();
    RequestRecord Rec(RequestStatus status) => new()
    {
      Id = Guid.NewGuid(),
      UserId = Guid.NewGuid(),
      TmdbId = 100,
      MediaType = "movie",
      Title = "X",
      Status = status,
    };
    var approved1 = await store.CreateAsync(Rec(RequestStatus.Approved), CancellationToken.None);
    var approved2 = await store.CreateAsync(Rec(RequestStatus.Approved), CancellationToken.None);
    await store.CreateAsync(Rec(RequestStatus.Pending), CancellationToken.None);
    await store.CreateAsync(Rec(RequestStatus.Available), CancellationToken.None);
    await store.CreateAsync(Rec(RequestStatus.Denied), CancellationToken.None);
    var dispatcher = new FakeDownloadDispatcher();

    var result = await CreateController(store, isAdmin: true, dispatcher: dispatcher).RetryAll(CancellationToken.None);

    var dto = Assert.IsType<RetryAllResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(2, dto.Total);
    Assert.Equal(2, dto.Retried);
    Assert.Equal(0, dto.Failed);
    // Only the two approved (stuck) requests were retried — never Pending/Available/Denied.
    Assert.Equal(2, dispatcher.Retried.Count);
    Assert.Contains(approved1.Id, dispatcher.Retried);
    Assert.Contains(approved2.Id, dispatcher.Retried);
  }

  [Fact]
  public async Task Retry_NotApproved_ReturnsConflict()
  {
    var store = new FakeRequestStore();
    var created = (RequestRecord)((OkObjectResult)(await CreateController(store).Create(ValidDto(), CancellationToken.None)).Result!).Value!;

    var result = await CreateController(store, isAdmin: true).Retry(created.Id, CancellationToken.None);

    Assert.IsType<ConflictObjectResult>(result.Result);
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

  [Fact]
  public async Task AssignOwner_GrantsAvailableOwnershipToTargetUser_AndNotifies()
  {
    var store = new FakeRequestStore();
    var target = Guid.NewGuid();
    var notifier = new RecordingNotifier();

    var result = await CreateController(store, notifications: notifier).AssignOwner(
      new AdminCreateRequestDto { UserId = target, TmdbId = 5, MediaType = "movie", Title = "Dune" },
      CancellationToken.None);

    var record = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(target, record.UserId);
    Assert.Equal(RequestStatus.Available, record.Status);
    Assert.Equal("item-5", record.JellyfinItemId); // ownership points at the resolved library item
    Assert.Equal(target, notifier.NotifiedUser);   // the assignee is told
  }

  [Fact]
  public async Task AssignOwner_SeasonScope_ResolvesTheSeasonItem()
  {
    var store = new FakeRequestStore();
    var result = await CreateController(store).AssignOwner(
      new AdminCreateRequestDto { UserId = Guid.NewGuid(), TmdbId = 9, MediaType = "tv", Title = "Show", Season = 2 },
      CancellationToken.None);

    var record = Assert.IsType<RequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal("season-9", record.JellyfinItemId); // FindSeasonItemId, not the whole series
    Assert.Equal(2, record.Season);
  }

  [Fact]
  public async Task AssignOwner_NotInLibrary_ReturnsBadRequest()
  {
    var result = await CreateController(new FakeRequestStore(), matcher: new NullLibraryMatcher()).AssignOwner(
      new AdminCreateRequestDto { UserId = Guid.NewGuid(), TmdbId = 5, MediaType = "movie", Title = "Dune" },
      CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task AssignOwner_MissingUser_ReturnsBadRequest()
  {
    var result = await CreateController(new FakeRequestStore()).AssignOwner(
      new AdminCreateRequestDto { UserId = Guid.Empty, TmdbId = 5, MediaType = "movie", Title = "Dune" },
      CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public async Task AssignOwner_AlreadyOwned_RenewsInsteadOfDuplicating()
  {
    var store = new FakeRequestStore();
    var target = Guid.NewGuid();
    var dto = new AdminCreateRequestDto { UserId = target, TmdbId = 5, MediaType = "movie", Title = "Dune" };

    await CreateController(store).AssignOwner(dto, CancellationToken.None);
    await CreateController(store).AssignOwner(dto, CancellationToken.None);

    Assert.Single(await store.GetByUserAsync(target, CancellationToken.None)); // renewed, not duplicated
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    private readonly Guid _userId;
    private readonly bool _isAdmin;

    public FakeUserAccessor(Guid userId, bool isAdmin = false)
    {
      _userId = userId;
      _isAdmin = isAdmin;
    }

    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(_userId);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(_isAdmin);
  }

  // ---------- Quota reservation is sized by what the request actually pulls ----------

  private static StubTmdbClient ThreeSeasonsOfTen()
    => new()
    {
      Seasons = new List<Season>
      {
        new() { SeasonNumber = 0, EpisodeCount = 5 },
        new() { SeasonNumber = 1, EpisodeCount = 10 },
        new() { SeasonNumber = 2, EpisodeCount = 10 },
        new() { SeasonNumber = 3, EpisodeCount = 10 },
      }
    };

  [Fact]
  public async Task Create_WholeSeries_ReservesEveryEpisodeAgainstTheQuota()
  {
    var quota = new FakeQuotaService(canRequest: true);
    var store = new FakeRequestStore();
    var controller = CreateController(store, tmdb: ThreeSeasonsOfTen(), quota: quota);

    var result = await controller.Create(
      new CreateRequestDto { TmdbId = 100604, MediaType = "tv", Title = "Stalk" },
      CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var created = Assert.IsType<RequestRecord>(ok.Value);
    Assert.Equal(30, quota.LastEpisodesRequested);  // not 1: the series is 30 episodes
    Assert.Equal(30, created.EstimatedEpisodes);    // ...and it stays reserved while in flight
  }

  [Fact]
  public async Task Create_Season_ReservesThatSeasonsEpisodes()
  {
    var quota = new FakeQuotaService(canRequest: true);
    var controller = CreateController(new FakeRequestStore(), tmdb: ThreeSeasonsOfTen(), quota: quota);

    await controller.Create(
      new CreateRequestDto { TmdbId = 100604, MediaType = "tv", Title = "Stalk", Season = 2 },
      CancellationToken.None);

    Assert.Equal(10, quota.LastEpisodesRequested);
  }

  [Fact]
  public async Task Create_SingleEpisodeOrMovie_ReservesOne()
  {
    var quota = new FakeQuotaService(canRequest: true);
    var controller = CreateController(new FakeRequestStore(), tmdb: ThreeSeasonsOfTen(), quota: quota);

    await controller.Create(
      new CreateRequestDto { TmdbId = 100604, MediaType = "tv", Title = "Stalk", Season = 2, Episode = 4 },
      CancellationToken.None);
    Assert.Equal(1, quota.LastEpisodesRequested);

    await controller.Create(
      new CreateRequestDto { TmdbId = 1372, MediaType = "movie", Title = "Blood Diamond" },
      CancellationToken.None);
    Assert.Equal(1, quota.LastEpisodesRequested);
  }

  private sealed class FakeQuotaService : IQuotaService
  {
    private readonly bool _canRequest;

    public FakeQuotaService(bool canRequest) => _canRequest = canRequest;

    /// <summary>Gets the number of times <see cref="CanRequestAsync"/> was invoked (the create-time quota gate).</summary>
    public int CanRequestCalls { get; private set; }

    public long GetQuotaBytes(Guid userId) => 0;

    public long GetBaseQuotaBytes(Guid userId) => 0;

public Task<IReadOnlyDictionary<Guid, QuotaInfo>> GetUsageAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyDictionary<Guid, QuotaInfo>>(userIds.ToDictionary(id => id, _ => new QuotaInfo()));

    public Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken)
      => Task.FromResult(new QuotaInfo());

    /// <summary>Gets the episode count the create-time quota gate was last asked to reserve.</summary>
    public int LastEpisodesRequested { get; private set; }

    public Task<bool> CanRequestAsync(Guid userId, string mediaType, int episodes, CancellationToken cancellationToken)
    {
      CanRequestCalls++;
      LastEpisodesRequested = episodes;
      return Task.FromResult(_canRequest);
    }

    public Task<bool> IsWithinQuotaAsync(Guid userId, CancellationToken cancellationToken)
      => Task.FromResult(_canRequest);
  }

  private sealed class FakeNotificationService : INotificationService
  {
    public Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task NotifyAvailableBatchAsync(System.Collections.Generic.IReadOnlyList<RequestRecord> requests, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task NotifyPersonalAsync(Guid userId, PersonalNotifyKind kind, string title, string subject, string body, string? posterPath, CancellationToken cancellationToken)
      => Task.CompletedTask;

    public Task SendTestAsync(string channel, CancellationToken cancellationToken) => Task.CompletedTask;


    public Task SendPersonalTestAsync(System.Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
  }

  private sealed class FakeDownloadDispatcher : IDownloadDispatcher
  {
    public List<Guid> Retried { get; } = new();

    public Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task DispatchDueAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TestActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> PurgeAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task RetryStuckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RescanAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> RetryAsync(RequestRecord request, CancellationToken cancellationToken)
    {
      Retried.Add(request.Id);
      return Task.FromResult(true);
    }
  }

  private sealed class FakeServarrStatusService : IServarrStatusService
  {
    public Task<IReadOnlyList<DownloadStatusDto>> GetStatusesAsync(IEnumerable<RequestRecord> requests, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<DownloadStatusDto>>(new List<DownloadStatusDto>());
  }

  // Records the personal notification so a test can assert the assignee was told.
  private sealed class RecordingNotifier : INotificationService
  {
    public Guid? NotifiedUser { get; private set; }

    public Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task NotifyAvailableBatchAsync(System.Collections.Generic.IReadOnlyList<RequestRecord> requests, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task NotifyPersonalAsync(Guid userId, PersonalNotifyKind kind, string title, string subject, string body, string? posterPath, CancellationToken cancellationToken)
    {
      NotifiedUser = userId;
      return Task.CompletedTask;
    }

    public Task SendTestAsync(string channel, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SendPersonalTestAsync(System.Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
  }

  // Nothing is in the library — every lookup misses.
  private sealed class NullLibraryMatcher : ILibraryMatcher
  {
    public bool Exists(string mediaType, int tmdbId) => false;

    public string? FindItemId(string mediaType, int tmdbId) => null;

    public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode) => null;

    public string? FindSeasonItemId(int seriesTmdbId, int season) => null;

    public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode) => 0;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;

    public System.Collections.Generic.IReadOnlyList<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem> ListLibraryMedia() => System.Array.Empty<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem>();
  }

  private sealed class FakeLibraryMatcher : ILibraryMatcher
  {
    public bool Exists(string mediaType, int tmdbId) => true;

    public string? FindItemId(string mediaType, int tmdbId) => "item-" + tmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode) => "item-" + seriesTmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string? FindSeasonItemId(int seriesTmdbId, int season) => "season-" + seriesTmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode) => 0;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;

    public System.Collections.Generic.IReadOnlyList<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem> ListLibraryMedia() => System.Array.Empty<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem>();
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

    public Task<RequestRecord?> PromoteFromQuotaHoldAsync(Guid id, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.Status != RequestStatus.Pending || !record.HeldForQuota)
      {
        return Task.FromResult<RequestRecord?>(null);
      }

      record.Status = RequestStatus.Approved;
      record.HeldForQuota = false;
      return Task.FromResult<RequestRecord?>(record);
    }

    public Task<RequestRecord?> HoldForQuotaAsync(Guid id, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.Status != RequestStatus.Approved || record.DispatchedAt is not null)
      {
        return Task.FromResult<RequestRecord?>(null);
      }

      record.Status = RequestStatus.Pending;
      record.HeldForQuota = true;
      return Task.FromResult<RequestRecord?>(record);
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

    public Task<RequestRecord?> RenewAvailableAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is not null)
      {
        record.AvailableAt = whenUtc;
        record.DeletionRequestedAt = null;
      }

      return Task.FromResult(record);
    }

    public Task<IReadOnlyList<RequestRecord>> ExpireOwnershipsAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
      var lapsed = _items.Where(r => r.Status == RequestStatus.Available && r.DeletionRequestedAt is null && r.AvailableAt is { } at && at < cutoffUtc).ToList();
      _items.RemoveAll(r => lapsed.Contains(r));
      return Task.FromResult<IReadOnlyList<RequestRecord>>(lapsed);
    }

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

    public Task<RequestRecord?> AdminFlagDeletionAsync(Guid id, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.Status != RequestStatus.Available || record.DeletionRequestedAt is not null)
      {
        return Task.FromResult<RequestRecord?>(null);
      }

      record.DeletionRequestedAt = DateTime.UtcNow;
      return Task.FromResult<RequestRecord?>(record);
    }

    public Task<RequestRecord?> CancelDeletionAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.UserId != userId || record.DeletionRequestedAt is null)
      {
        return Task.FromResult<RequestRecord?>(null);
      }

      record.DeletionRequestedAt = null;
      return Task.FromResult<RequestRecord?>(record);
    }

    public Task<bool> AnyActiveReferenceAsync(Guid excludeId, int tmdbId, string mediaType, int? season, int? episode, CancellationToken cancellationToken)
      => Task.FromResult(_items.Any(r => r.Id != excludeId && r.TmdbId == tmdbId
        && string.Equals(r.MediaType, mediaType, StringComparison.Ordinal)
        && r.DeletionRequestedAt is null && r.Status != RequestStatus.Denied
        && MediaScope.Overlaps(season, episode, r.Season, r.Episode)));

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

    public Task<RequestRecord?> SetDispatchErrorAsync(Guid id, string? error, DateTime whenUtc, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is not null)
      {
        record.DispatchError = error;
        record.DispatchAttemptedAt = whenUtc;
      }

      return Task.FromResult(record);
    }

    public Task<RequestRecord?> SetJellyfinItemIdAsync(Guid id, string jellyfinItemId, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is null || string.Equals(record.JellyfinItemId, jellyfinItemId, StringComparison.OrdinalIgnoreCase))
      {
        return Task.FromResult<RequestRecord?>(null);
      }

      record.JellyfinItemId = jellyfinItemId;
      return Task.FromResult<RequestRecord?>(record);
    }

    public Task<RequestRecord?> MarkNotFoundNotifiedAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken)
    {
      var record = _items.FirstOrDefault(r => r.Id == id);
      if (record is null || record.NotFoundNotifiedAt is not null)
      {
        return Task.FromResult<RequestRecord?>(null);
      }

      record.NotFoundNotifiedAt = whenUtc;
      return Task.FromResult<RequestRecord?>(record);
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
