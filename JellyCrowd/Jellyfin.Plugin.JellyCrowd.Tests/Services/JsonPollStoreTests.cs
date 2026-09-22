using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonPollStore"/>.
/// </summary>
public sealed class JsonPollStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonPollStore _store;

  public JsonPollStoreTests() => _store = new JsonPollStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private static Poll New(string question = "Movie night?")
  {
    var poll = new Poll { Question = question };
    poll.Options.Add(new PollOption { Id = Guid.NewGuid(), Text = "Friday" });
    poll.Options.Add(new PollOption { Id = Guid.NewGuid(), Text = "Saturday" });
    return poll;
  }

  private static PollVote Ballot(Guid userId, Guid optionId, string name = "u")
  {
    var vote = new PollVote { UserId = userId, UserName = name };
    vote.OptionIds.Add(optionId);
    return vote;
  }

  [Fact]
  public async Task AddAsync_AssignsIdAndTimestamp()
  {
    var added = await _store.AddAsync(New(), CancellationToken.None);

    Assert.NotEqual(Guid.Empty, added.Id);
    Assert.NotEqual(default, added.CreatedAt);
    Assert.Equal(added.Id, (await _store.GetByIdAsync(added.Id, CancellationToken.None))!.Id);
  }

  [Fact]
  public async Task GetAllAsync_NewestFirst()
  {
    var old = await _store.AddAsync(New("old"), CancellationToken.None);
    old.CreatedAt = DateTime.UtcNow.AddDays(-2);
    var recent = await _store.AddAsync(New("recent"), CancellationToken.None);

    var all = await _store.GetAllAsync(CancellationToken.None);
    Assert.Equal(recent.Id, all[0].Id);
    Assert.Equal(old.Id, all[1].Id);
  }

  [Fact]
  public async Task VoteAsync_ReplacesTheVotersPreviousBallot()
  {
    var poll = await _store.AddAsync(New(), CancellationToken.None);
    var user = Guid.NewGuid();

    await _store.VoteAsync(poll.Id, Ballot(user, poll.Options[0].Id), CancellationToken.None);
    var updated = await _store.VoteAsync(poll.Id, Ballot(user, poll.Options[1].Id), CancellationToken.None);

    var vote = Assert.Single(updated!.Votes);
    Assert.Equal(poll.Options[1].Id, Assert.Single(vote.OptionIds));
    Assert.NotEqual(default, vote.VotedAt);
  }

  [Fact]
  public async Task VoteAsync_KeepsOneBallotPerVoter()
  {
    var poll = await _store.AddAsync(New(), CancellationToken.None);
    await _store.VoteAsync(poll.Id, Ballot(Guid.NewGuid(), poll.Options[0].Id), CancellationToken.None);
    var updated = await _store.VoteAsync(poll.Id, Ballot(Guid.NewGuid(), poll.Options[0].Id), CancellationToken.None);

    Assert.Equal(2, updated!.Votes.Count);
  }

  [Fact]
  public async Task VoteAsync_UnknownPoll_ReturnsNull()
    => Assert.Null(await _store.VoteAsync(Guid.NewGuid(), Ballot(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

  [Fact]
  public async Task UpdateAsync_RewritesThePoll_UntilTheFirstVote()
  {
    var poll = await _store.AddAsync(New(), CancellationToken.None);
    var edit = New("Board game night?");
    edit.MultiChoice = true;

    var updated = await _store.UpdateAsync(poll.Id, edit, CancellationToken.None);
    Assert.Equal("Board game night?", updated!.Question);
    Assert.True(updated.MultiChoice);

    await _store.VoteAsync(poll.Id, Ballot(Guid.NewGuid(), updated.Options[0].Id), CancellationToken.None);
    var refused = await _store.UpdateAsync(poll.Id, New("Too late"), CancellationToken.None);
    Assert.Equal("Board game night?", refused!.Question);
  }

  [Fact]
  public async Task SetClosedAsync_StampsTheClosingTime_AndDropsAStaleDeadlineOnReopen()
  {
    var poll = await _store.AddAsync(New(), CancellationToken.None);
    poll.ClosesAt = DateTime.UtcNow.AddHours(-1);

    var closed = await _store.SetClosedAsync(poll.Id, closed: true, CancellationToken.None);
    Assert.True(closed!.Closed);
    Assert.NotNull(closed.ClosedAt);

    var reopened = await _store.SetClosedAsync(poll.Id, closed: false, CancellationToken.None);
    Assert.False(reopened!.Closed);
    Assert.Null(reopened.ClosedAt);
    Assert.Null(reopened.ClosesAt); // a deadline already passed would close it again on the spot
  }

  [Fact]
  public async Task SetClosedAsync_KeepsADeadlineStillAhead()
  {
    var poll = await _store.AddAsync(New(), CancellationToken.None);
    var deadline = DateTime.UtcNow.AddDays(3);
    poll.ClosesAt = deadline;

    await _store.SetClosedAsync(poll.Id, closed: true, CancellationToken.None);
    var reopened = await _store.SetClosedAsync(poll.Id, closed: false, CancellationToken.None);

    Assert.Equal(deadline, reopened!.ClosesAt);
  }

  [Fact]
  public async Task DeleteAsync_RemovesThePollAndItsVotes()
  {
    var poll = await _store.AddAsync(New(), CancellationToken.None);
    await _store.VoteAsync(poll.Id, Ballot(Guid.NewGuid(), poll.Options[0].Id), CancellationToken.None);

    Assert.True(await _store.DeleteAsync(poll.Id, CancellationToken.None));
    Assert.Null(await _store.GetByIdAsync(poll.Id, CancellationToken.None));
    Assert.False(await _store.DeleteAsync(poll.Id, CancellationToken.None));
  }

  [Fact]
  public async Task AddAsync_OverTheCap_DropsClosedPollsBeforeOpenOnes()
  {
    // 100 closed polls, then one more: the cap must bite on the history, never on the live poll.
    for (var i = 0; i < 100; i++)
    {
      var closed = New("closed " + i);
      closed.Closed = true;
      closed.CreatedAt = DateTime.UtcNow.AddDays(-100 + i);
      await _store.AddAsync(closed, CancellationToken.None);
    }

    var live = await _store.AddAsync(New("live"), CancellationToken.None);
    var all = await _store.GetAllAsync(CancellationToken.None);

    Assert.Equal(100, all.Count);
    Assert.Contains(all, p => p.Id == live.Id);
    Assert.DoesNotContain(all, p => p.Question == "closed 0");
  }

  [Fact]
  public async Task Store_SurvivesAReload()
  {
    var poll = await _store.AddAsync(New(), CancellationToken.None);
    await _store.VoteAsync(poll.Id, Ballot(Guid.NewGuid(), poll.Options[0].Id, "alice"), CancellationToken.None);

    using var reopened = new JsonPollStore(_path);
    var loaded = await reopened.GetByIdAsync(poll.Id, CancellationToken.None);

    Assert.Equal("Movie night?", loaded!.Question);
    Assert.Equal("alice", Assert.Single(loaded.Votes).UserName);
    Assert.Equal(2, loaded.Options.Count);
    Assert.Equal(poll.Options[0].Id, loaded.Votes.Single().OptionIds.Single());
  }
}
