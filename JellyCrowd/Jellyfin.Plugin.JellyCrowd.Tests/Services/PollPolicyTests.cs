using System;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="PollPolicy"/>: who may vote, who may see the tally, and what a valid ballot is.
/// </summary>
public class PollPolicyTests
{
  private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
  private static readonly Guid User = Guid.NewGuid();

  private static Poll Poll(params string[] options)
  {
    var poll = new Poll { Id = Guid.NewGuid(), Question = "Movie night?", CreatedAt = Now.AddDays(-1) };
    foreach (var option in options.Length > 0 ? options : new[] { "Friday", "Saturday" })
    {
      poll.Options.Add(new PollOption { Id = Guid.NewGuid(), Text = option });
    }

    return poll;
  }

  private static void Vote(Poll poll, Guid userId, params Guid[] optionIds)
  {
    var vote = new PollVote { UserId = userId, UserName = "u" + userId.ToString()[..4], VotedAt = Now };
    foreach (var optionId in optionIds)
    {
      vote.OptionIds.Add(optionId);
    }

    poll.Votes.Add(vote);
  }

  private static (PluginConfiguration Config, Guid Member) WithGroup(bool childMode = false)
  {
    var member = Guid.NewGuid();
    var group = new UserGroup { Id = Guid.NewGuid(), Name = "Family", ChildMode = childMode };
    group.Members.Add(member);
    var config = new PluginConfiguration();
    config.UserGroups.Add(group);
    return (config, member);
  }

  [Fact]
  public void IsOpen_UntilClosedByHandOrPastItsDeadline()
  {
    var poll = Poll();
    Assert.True(PollPolicy.IsOpen(poll, Now));

    poll.ClosesAt = Now.AddHours(1);
    Assert.True(PollPolicy.IsOpen(poll, Now));

    poll.ClosesAt = Now.AddHours(-1);
    Assert.False(PollPolicy.IsOpen(poll, Now));

    poll.ClosesAt = null;
    poll.Closed = true;
    Assert.False(PollPolicy.IsOpen(poll, Now));
  }

  [Fact]
  public void CanVote_GlobalPollReachesEveryone_TargetedOneOnlyItsGroup()
  {
    var (config, member) = WithGroup();
    var poll = Poll();
    Assert.True(PollPolicy.CanVote(config, poll, User, Now));

    poll.GroupIds.Add(config.UserGroups[0].Id);
    Assert.True(PollPolicy.CanVote(config, poll, member, Now));
    Assert.False(PollPolicy.CanVote(config, poll, User, Now));
  }

  [Fact]
  public void CanVote_FalseForChildAccountsAndClosedPolls()
  {
    var (config, child) = WithGroup(childMode: true);
    var poll = Poll();
    Assert.False(PollPolicy.CanVote(config, poll, child, Now));

    poll.Closed = true;
    Assert.False(PollPolicy.CanVote(new PluginConfiguration(), poll, User, Now));
  }

  [Fact]
  public void ShouldSee_AdminSeesEverything_IncludingAPollAddressedToOthers()
  {
    var (config, _) = WithGroup();
    var poll = Poll();
    poll.GroupIds.Add(config.UserGroups[0].Id);

    Assert.False(PollPolicy.ShouldSee(config, poll, User, isAdmin: false, Now));
    Assert.True(PollPolicy.ShouldSee(config, poll, User, isAdmin: true, Now));
    // Seeing it is not voting in it: an admin outside the audience must not skew someone else's result.
    Assert.False(PollPolicy.CanVote(config, poll, User, Now));
  }

  [Fact]
  public void ShouldSee_ClosedPollKeepsShowingItsResultForTheGraceWindowOnly()
  {
    var config = new PluginConfiguration();
    var poll = Poll();
    poll.Closed = true;
    poll.ClosedAt = Now.AddDays(-1);
    Assert.True(PollPolicy.ShouldSee(config, poll, User, isAdmin: false, Now));

    poll.ClosedAt = Now.AddDays(-(PollPolicy.ClosedResultsGraceDays + 1));
    Assert.False(PollPolicy.ShouldSee(config, poll, User, isAdmin: false, Now));

    // A poll that never shares its result has nothing left to show the moment it closes.
    poll.ClosedAt = Now.AddDays(-1);
    poll.ShowResults = false;
    Assert.False(PollPolicy.ShouldSee(config, poll, User, isAdmin: false, Now));
  }

  [Fact]
  public void ResultsVisibleTo_OnlyAfterVoting_OrOnceClosed()
  {
    var poll = Poll();
    Assert.False(PollPolicy.ResultsVisibleTo(poll, User, isAdmin: false, Now));
    Assert.True(PollPolicy.ResultsVisibleTo(poll, User, isAdmin: true, Now));

    Vote(poll, User, poll.Options[0].Id);
    Assert.True(PollPolicy.ResultsVisibleTo(poll, User, isAdmin: false, Now));

    var other = Poll();
    other.Closed = true;
    Assert.True(PollPolicy.ResultsVisibleTo(other, User, isAdmin: false, Now));

    other.ShowResults = false;
    Assert.False(PollPolicy.ResultsVisibleTo(other, User, isAdmin: false, Now));
  }

  [Fact]
  public void Tally_CountsEachVoterOnce_AndKeepsTheOptionsNobodyPicked()
  {
    var poll = Poll("A", "B", "C");
    var a = poll.Options[0].Id;
    var b = poll.Options[1].Id;
    poll.MultiChoice = true;
    Vote(poll, Guid.NewGuid(), a, b);
    Vote(poll, Guid.NewGuid(), a, a); // a duplicated pick is still one voter
    Vote(poll, Guid.NewGuid(), Guid.NewGuid()); // an option that no longer exists is ignored

    var tally = PollPolicy.Tally(poll);
    Assert.Equal(2, tally[a]);
    Assert.Equal(1, tally[b]);
    Assert.Equal(0, tally[poll.Options[2].Id]);
    Assert.Equal(3, tally.Count);
  }

  [Fact]
  public void Percent_IsAShareOfVoters_NotOfPicks()
  {
    Assert.Equal(0, PollPolicy.Percent(0, 0));
    Assert.Equal(50, PollPolicy.Percent(1, 2));
    Assert.Equal(33, PollPolicy.Percent(1, 3));
    // Multi-choice: every voter picked both options, so both are at 100%.
    Assert.Equal(100, PollPolicy.Percent(2, 2));
  }

  [Fact]
  public void ValidateVote_RejectsClosedEmptyOverfilledAndUnknownBallots()
  {
    var poll = Poll();
    var first = poll.Options[0].Id;
    var second = poll.Options[1].Id;

    Assert.Null(PollPolicy.ValidateVote(poll, new[] { first }, Now));
    Assert.Equal(PollPolicy.ErrorEmpty, PollPolicy.ValidateVote(poll, Array.Empty<Guid>(), Now));
    Assert.Equal(PollPolicy.ErrorSingleChoice, PollPolicy.ValidateVote(poll, new[] { first, second }, Now));
    Assert.Equal(PollPolicy.ErrorUnknownOption, PollPolicy.ValidateVote(poll, new[] { Guid.NewGuid() }, Now));

    poll.MultiChoice = true;
    Assert.Null(PollPolicy.ValidateVote(poll, new[] { first, second }, Now));
    // The same option twice is one pick, not two: it must not trip the single-choice rule.
    poll.MultiChoice = false;
    Assert.Null(PollPolicy.ValidateVote(poll, new[] { first, first }, Now));

    poll.Closed = true;
    Assert.Equal(PollPolicy.ErrorClosed, PollPolicy.ValidateVote(poll, new[] { first }, Now));
  }

  [Fact]
  public void HasVoted_TracksTheVoter()
  {
    var poll = Poll();
    Assert.False(PollPolicy.HasVoted(poll, User));
    Vote(poll, User, poll.Options.First().Id);
    Assert.True(PollPolicy.HasVoted(poll, User));
  }
}
