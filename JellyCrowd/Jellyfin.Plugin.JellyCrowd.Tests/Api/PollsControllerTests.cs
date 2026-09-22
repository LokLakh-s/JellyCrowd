using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Jellyfin.Plugin.JellyCrowd.Tests.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="PollsController"/>, against real JSON stores.
/// </summary>
public sealed class PollsControllerTests : IDisposable
{
  private static readonly Guid Voter = Guid.NewGuid();
  private static readonly Guid Other = Guid.NewGuid();

  private readonly string _pollPath = Path.Combine(Path.GetTempPath(), "jc-polls-" + Guid.NewGuid() + ".json");
  private readonly string _notifPath = Path.Combine(Path.GetTempPath(), "jc-notif-" + Guid.NewGuid() + ".json");
  private readonly JsonPollStore _polls;
  private readonly JsonUserNotificationStore _notifications;
  private readonly PluginConfiguration _config = new();

  public PollsControllerTests()
  {
    _polls = new JsonPollStore(_pollPath);
    _notifications = new JsonUserNotificationStore(_notifPath);
  }

  public void Dispose()
  {
    _polls.Dispose();
    _notifications.Dispose();
    foreach (var path in new[] { _pollPath, _notifPath })
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
  }

  private PollsController Controller(Guid userId, bool isAdmin = false)
  {
    var users = new Mock<IUserManager>();
    users.Setup(m => m.GetUsers()).Returns(new List<User>
    {
      new("voter", "Prov", "Prov") { Id = Voter },
      new("other", "Prov", "Prov") { Id = Other }
    });

    return new PollsController(
      _polls,
      new FakeUserAccessor(userId, isAdmin),
      _notifications,
      new NoOpActivityLog(),
      users.Object,
      id => id == Voter ? "voter" : "other",
      () => _config)
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
  }

  private static PollDto NewDto(params string[] options)
  {
    var dto = new PollDto { Question = "  Movie night?  " };
    foreach (var option in options.Length > 0 ? options : new[] { "Friday", "Saturday" })
    {
      dto.Options.Add(option);
    }

    return dto;
  }

  private async Task<PollAdminDto> CreateAsync(PollDto? dto = null, Guid? adminId = null)
  {
    var result = await Controller(adminId ?? Other, isAdmin: true).Create(dto ?? NewDto(), CancellationToken.None);
    return Assert.IsType<PollAdminDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
  }

  private static Collection<Guid> Pick(params Guid[] ids)
  {
    var picked = new Collection<Guid>();
    foreach (var id in ids)
    {
      picked.Add(id);
    }

    return picked;
  }

  private Guid AddGroup(params Guid[] members)
  {
    var group = new UserGroup { Id = Guid.NewGuid(), Name = "Family" };
    foreach (var member in members)
    {
      group.Members.Add(member);
    }

    _config.UserGroups.Add(group);
    return group.Id;
  }

  [Fact]
  public async Task Create_TrimsTheQuestion_AndGivesEveryOptionAnId()
  {
    var created = await CreateAsync();

    Assert.Equal("Movie night?", created.Question);
    Assert.Equal(2, created.Options.Count);
    Assert.All(created.Options, o => Assert.NotEqual(Guid.Empty, o.Id));
    Assert.True(created.Open);
    Assert.True(created.Editable);
  }

  [Fact]
  public async Task Create_NeedsAQuestionAndTwoOptions()
  {
    var noQuestion = await Controller(Other, isAdmin: true).Create(new PollDto { Question = "   " }, CancellationToken.None);
    Assert.IsType<BadRequestObjectResult>(noQuestion.Result);

    var oneOption = await Controller(Other, isAdmin: true).Create(NewDto("Friday"), CancellationToken.None);
    Assert.IsType<BadRequestObjectResult>(oneOption.Result);

    // Blank options are dropped before counting, so "two" must mean two real answers.
    var blank = await Controller(Other, isAdmin: true).Create(NewDto("Friday", "   "), CancellationToken.None);
    Assert.IsType<BadRequestObjectResult>(blank.Result);
  }

  [Fact]
  public async Task Create_ADeadlineAlreadyPassed_IsDropped()
  {
    var dto = NewDto();
    dto.ClosesAt = DateTime.UtcNow.AddHours(-1);

    var created = await CreateAsync(dto);

    Assert.Null(created.ClosesAt); // a poll nobody could answer is not what the admin meant
    Assert.True(created.Open);
  }

  [Fact]
  public async Task Create_NotifiesTheAudienceExceptItsAuthor()
  {
    await CreateAsync(adminId: Other);

    var forVoter = await _notifications.GetByUserAsync(Voter, CancellationToken.None);
    var notification = Assert.Single(forVoter);
    Assert.Equal("Poll", notification.Event);
    Assert.Equal("Movie night?", notification.Title);
    Assert.NotNull(notification.RefId);

    Assert.Empty(await _notifications.GetByUserAsync(Other, CancellationToken.None));
  }

  [Fact]
  public async Task Create_TargetedPoll_OnlyNotifiesItsGroup()
  {
    var dto = NewDto();
    dto.GroupIds.Add(AddGroup(Voter));

    await CreateAsync(dto, adminId: Guid.NewGuid());

    Assert.Single(await _notifications.GetByUserAsync(Voter, CancellationToken.None));
    Assert.Empty(await _notifications.GetByUserAsync(Other, CancellationToken.None));
  }

  [Fact]
  public async Task Mine_HidesTheTallyUntilTheUserHasVoted()
  {
    var created = await CreateAsync();
    var before = Assert.IsType<OkObjectResult>((await Controller(Voter).Mine(CancellationToken.None)).Result).Value as IReadOnlyList<PollViewDto>;
    var poll = Assert.Single(before!);

    Assert.False(poll.ResultsVisible);
    Assert.Equal(0, poll.TotalVotes);
    Assert.All(poll.Options, o => Assert.Equal(0, o.Votes));
    Assert.True(poll.CanVote);
    Assert.False(poll.Voted);

    await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[0].Id) }, CancellationToken.None);

    var after = Assert.IsType<OkObjectResult>((await Controller(Voter).Mine(CancellationToken.None)).Result).Value as IReadOnlyList<PollViewDto>;
    var voted = Assert.Single(after!);
    Assert.True(voted.ResultsVisible);
    Assert.Equal(1, voted.TotalVotes);
    Assert.Equal(100, voted.Options[0].Percent);
    Assert.Equal(created.Options[0].Id, Assert.Single(voted.MyOptionIds));
  }

  [Fact]
  public async Task Mine_APollWithoutSharedResults_StaysSilentEvenAfterVoting()
  {
    var dto = NewDto();
    dto.ShowResults = false;
    var created = await CreateAsync(dto);
    await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[0].Id) }, CancellationToken.None);

    var mine = Assert.IsType<OkObjectResult>((await Controller(Voter).Mine(CancellationToken.None)).Result).Value as IReadOnlyList<PollViewDto>;
    var poll = Assert.Single(mine!);

    Assert.True(poll.Voted);
    Assert.False(poll.ResultsVisible);
    Assert.Equal(0, poll.TotalVotes);
  }

  [Fact]
  public async Task Mine_TargetedPoll_IsNotDeliveredToOutsiders_ButIsToAdmins()
  {
    var dto = NewDto();
    dto.GroupIds.Add(AddGroup(Voter));
    await CreateAsync(dto, adminId: Guid.NewGuid());

    var outsider = Assert.IsType<OkObjectResult>((await Controller(Other).Mine(CancellationToken.None)).Result).Value as IReadOnlyList<PollViewDto>;
    Assert.Empty(outsider!);

    var admin = Assert.IsType<OkObjectResult>((await Controller(Other, isAdmin: true).Mine(CancellationToken.None)).Result).Value as IReadOnlyList<PollViewDto>;
    var seen = Assert.Single(admin!);
    Assert.False(seen.CanVote); // seeing someone else's poll is not voting in it

    var member = Assert.IsType<OkObjectResult>((await Controller(Voter).Mine(CancellationToken.None)).Result).Value as IReadOnlyList<PollViewDto>;
    Assert.True(Assert.Single(member!).CanVote);
  }

  [Fact]
  public async Task Vote_OutsideTheAudience_IsForbidden()
  {
    var dto = NewDto();
    dto.GroupIds.Add(AddGroup(Voter));
    var created = await CreateAsync(dto, adminId: Guid.NewGuid());

    var result = await Controller(Other, isAdmin: true).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[0].Id) }, CancellationToken.None);

    var status = Assert.IsType<ObjectResult>(result.Result);
    Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
  }

  [Fact]
  public async Task Vote_ReplacesThePreviousAnswer()
  {
    var created = await CreateAsync();
    await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[0].Id) }, CancellationToken.None);
    var second = await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[1].Id) }, CancellationToken.None);

    var view = Assert.IsType<PollViewDto>(Assert.IsType<OkObjectResult>(second.Result).Value);
    Assert.Equal(1, view.TotalVotes);
    Assert.Equal(created.Options[1].Id, Assert.Single(view.MyOptionIds));
    Assert.Equal(0, view.Options[0].Votes);
  }

  [Fact]
  public async Task Vote_RejectsAnEmptyOrOverfilledBallot()
  {
    var created = await CreateAsync();

    var empty = await Controller(Voter).Vote(created.Id, new PollVoteDto(), CancellationToken.None);
    Assert.IsType<BadRequestObjectResult>(empty.Result);

    var two = await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[0].Id, created.Options[1].Id) }, CancellationToken.None);
    Assert.IsType<BadRequestObjectResult>(two.Result);

    var unknown = await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(Guid.NewGuid()) }, CancellationToken.None);
    Assert.IsType<BadRequestObjectResult>(unknown.Result);
  }

  [Fact]
  public async Task Vote_MultiChoicePoll_TakesSeveralAnswers()
  {
    var dto = NewDto();
    dto.MultiChoice = true;
    var created = await CreateAsync(dto);

    var result = await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[0].Id, created.Options[1].Id) }, CancellationToken.None);

    var view = Assert.IsType<PollViewDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal(2, view.MyOptionIds.Count);
    Assert.Equal(1, view.TotalVotes); // one voter, two picks
    Assert.All(view.Options, o => Assert.Equal(100, o.Percent));
  }

  [Fact]
  public async Task Vote_OnAClosedPoll_Conflicts()
  {
    var created = await CreateAsync();
    await Controller(Other, isAdmin: true).Close(created.Id, CancellationToken.None);

    var result = await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[0].Id) }, CancellationToken.None);

    Assert.IsType<ConflictObjectResult>(result.Result);
  }

  [Fact]
  public async Task Vote_UnknownPoll_NotFound()
  {
    var result = await Controller(Voter).Vote(Guid.NewGuid(), new PollVoteDto { OptionIds = Pick(Guid.NewGuid()) }, CancellationToken.None);
    Assert.IsType<NotFoundResult>(result.Result);
  }

  [Fact]
  public async Task Update_RewritesThePoll_WhileNobodyHasVoted()
  {
    var created = await CreateAsync();
    var dto = NewDto("Sunday", "Monday");
    dto.Question = "Board game night?";

    var edited = await Controller(Other, isAdmin: true).Update(created.Id, dto, CancellationToken.None);

    var updated = Assert.IsType<PollAdminDto>(Assert.IsType<OkObjectResult>(edited.Result).Value);
    Assert.Equal("Board game night?", updated.Question);
    Assert.Equal(new[] { "Sunday", "Monday" }, updated.Options.Select(o => o.Text));
  }

  [Fact]
  public async Task Update_IsRefusedOnceSomeoneHasVoted()
  {
    var created = await CreateAsync();
    await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[0].Id) }, CancellationToken.None);

    var refused = await Controller(Other, isAdmin: true).Update(created.Id, NewDto("Too", "Late"), CancellationToken.None);

    Assert.IsType<ConflictObjectResult>(refused.Result);
  }

  [Fact]
  public async Task Update_UnknownPoll_NotFound()
  {
    var result = await Controller(Other, isAdmin: true).Update(Guid.NewGuid(), NewDto(), CancellationToken.None);
    Assert.IsType<NotFoundResult>(result.Result);
  }

  [Fact]
  public async Task CloseAndReopen_FlipWhetherThePollTakesVotes()
  {
    var created = await CreateAsync();

    var closed = Assert.IsType<PollAdminDto>(Assert.IsType<OkObjectResult>((await Controller(Other, isAdmin: true).Close(created.Id, CancellationToken.None)).Result).Value);
    Assert.True(closed.Closed);
    Assert.False(closed.Open);

    var reopened = Assert.IsType<PollAdminDto>(Assert.IsType<OkObjectResult>((await Controller(Other, isAdmin: true).Reopen(created.Id, CancellationToken.None)).Result).Value);
    Assert.False(reopened.Closed);
    Assert.True(reopened.Open);

    Assert.IsType<NotFoundResult>((await Controller(Other, isAdmin: true).Close(Guid.NewGuid(), CancellationToken.None)).Result);
  }

  [Fact]
  public async Task GetAll_ShowsWhoVotedWhatAndWhoHasNot()
  {
    var created = await CreateAsync();
    await Controller(Voter).Vote(created.Id, new PollVoteDto { OptionIds = Pick(created.Options[0].Id) }, CancellationToken.None);

    var all = Assert.IsType<OkObjectResult>((await Controller(Other, isAdmin: true).GetAll(CancellationToken.None)).Result).Value as IReadOnlyList<PollAdminDto>;
    var poll = Assert.Single(all!);

    Assert.Equal(1, poll.TotalVotes);
    Assert.Equal("voter", Assert.Single(poll.Options[0].Voters));
    Assert.Empty(poll.Options[1].Voters);
    Assert.Equal("other", Assert.Single(poll.NotVoted));
    Assert.False(poll.Editable);
  }

  [Fact]
  public async Task Delete_RemovesThePoll()
  {
    var created = await CreateAsync();

    Assert.IsType<NoContentResult>(await Controller(Other, isAdmin: true).Delete(created.Id, CancellationToken.None));
    Assert.IsType<NotFoundResult>(await Controller(Other, isAdmin: true).Delete(created.Id, CancellationToken.None));

    var all = Assert.IsType<OkObjectResult>((await Controller(Other, isAdmin: true).GetAll(CancellationToken.None)).Result).Value as IReadOnlyList<PollAdminDto>;
    Assert.Empty(all!);
  }

  [Fact]
  public async Task ChildAccounts_NeitherSeeNorAreNotifiedOfPolls()
  {
    var group = new UserGroup { Id = Guid.NewGuid(), Name = "Kids", ChildMode = true };
    group.Members.Add(Voter);
    _config.UserGroups.Add(group);

    await CreateAsync(adminId: Guid.NewGuid());

    Assert.Empty(await _notifications.GetByUserAsync(Voter, CancellationToken.None));
    var mine = Assert.IsType<OkObjectResult>((await Controller(Voter).Mine(CancellationToken.None)).Result).Value as IReadOnlyList<PollViewDto>;
    Assert.Empty(mine!);
  }

  private sealed class FakeUserAccessor : ICurrentUserAccessor
  {
    private readonly Guid _userId;
    private readonly bool _isAdmin;

    public FakeUserAccessor(Guid userId, bool isAdmin)
    {
      _userId = userId;
      _isAdmin = isAdmin;
    }

    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(_userId);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(_isAdmin);
  }
}
