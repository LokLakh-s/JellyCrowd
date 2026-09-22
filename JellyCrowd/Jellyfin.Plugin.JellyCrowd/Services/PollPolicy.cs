using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure decisions about a poll: whether it still accepts votes, who may vote in it, who may see its
/// result, and whether a submitted ballot is acceptable. Kept free of storage and HTTP so the rules
/// that decide what a user sees are unit-testable on their own.
/// </summary>
public static class PollPolicy
{
  /// <summary>Rejected ballot: the poll no longer accepts votes.</summary>
  public const string ErrorClosed = "closed";

  /// <summary>Rejected ballot: no option was picked.</summary>
  public const string ErrorEmpty = "empty";

  /// <summary>Rejected ballot: several options were picked on a single-choice poll.</summary>
  public const string ErrorSingleChoice = "single_choice";

  /// <summary>Rejected ballot: an option id does not belong to this poll.</summary>
  public const string ErrorUnknownOption = "unknown_option";

  /// <summary>
  /// How long a closed poll keeps showing its result to the users who could vote in it. Without this a
  /// poll would vanish the moment it closes, and nobody but the admin would ever learn the outcome.
  /// </summary>
  public const int ClosedResultsGraceDays = 7;

  /// <summary>
  /// Whether the poll still accepts votes: not closed by hand, and not past its deadline.
  /// </summary>
  /// <param name="poll">The poll.</param>
  /// <param name="utcNow">The current UTC time.</param>
  /// <returns><c>true</c> when the poll is open.</returns>
  public static bool IsOpen(Poll poll, DateTime utcNow)
  {
    ArgumentNullException.ThrowIfNull(poll);
    return !poll.Closed && (poll.ClosesAt is null || poll.ClosesAt > utcNow);
  }

  /// <summary>
  /// Whether the user may cast a vote: they are in the poll's audience (same rule as a targeted
  /// announcement — global polls reach everyone, child accounts never) and the poll is open. Being an
  /// administrator is deliberately not enough: an admin outside the audience watches the poll they
  /// published without adding their own answer to someone else's group result.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="poll">The poll.</param>
  /// <param name="userId">The user id.</param>
  /// <param name="utcNow">The current UTC time.</param>
  /// <returns><c>true</c> when the user may vote.</returns>
  public static bool CanVote(PluginConfiguration config, Poll poll, Guid userId, DateTime utcNow)
  {
    ArgumentNullException.ThrowIfNull(poll);
    return IsOpen(poll, utcNow) && RequestPolicy.IsInAudience(config, poll.GroupIds, userId);
  }

  /// <summary>
  /// Whether the poll should be delivered to the user at all: their own open polls, the closed ones
  /// whose result they are still allowed to read, and — for an administrator — everything.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="poll">The poll.</param>
  /// <param name="userId">The user id.</param>
  /// <param name="isAdmin">Whether the user is an administrator.</param>
  /// <param name="utcNow">The current UTC time.</param>
  /// <returns><c>true</c> when the poll should be shown to the user.</returns>
  public static bool ShouldSee(PluginConfiguration config, Poll poll, Guid userId, bool isAdmin, DateTime utcNow)
  {
    ArgumentNullException.ThrowIfNull(poll);
    if (isAdmin)
    {
      return true;
    }

    if (!RequestPolicy.IsInAudience(config, poll.GroupIds, userId))
    {
      return false;
    }

    if (IsOpen(poll, utcNow))
    {
      return true;
    }

    // Closed: only worth showing while the result is still readable and fresh.
    return poll.ShowResults && ClosedAt(poll).AddDays(ClosedResultsGraceDays) > utcNow;
  }

  /// <summary>
  /// Whether the user may see the tally: administrators always; everyone else only when the poll shows
  /// its results, and only once they have voted (or the poll has closed and there is nothing left to
  /// influence).
  /// </summary>
  /// <param name="poll">The poll.</param>
  /// <param name="userId">The user id.</param>
  /// <param name="isAdmin">Whether the user is an administrator.</param>
  /// <param name="utcNow">The current UTC time.</param>
  /// <returns><c>true</c> when the tally may be sent to this user.</returns>
  public static bool ResultsVisibleTo(Poll poll, Guid userId, bool isAdmin, DateTime utcNow)
  {
    ArgumentNullException.ThrowIfNull(poll);
    if (isAdmin)
    {
      return true;
    }

    return poll.ShowResults && (HasVoted(poll, userId) || !IsOpen(poll, utcNow));
  }

  /// <summary>
  /// Whether the user already voted in this poll.
  /// </summary>
  /// <param name="poll">The poll.</param>
  /// <param name="userId">The user id.</param>
  /// <returns><c>true</c> when a vote from this user is on record.</returns>
  public static bool HasVoted(Poll poll, Guid userId)
  {
    ArgumentNullException.ThrowIfNull(poll);
    return poll.Votes.Any(v => v.UserId == userId);
  }

  /// <summary>
  /// Counts the votes per option, including the options nobody picked. Votes referring to an option
  /// that no longer exists are ignored.
  /// </summary>
  /// <param name="poll">The poll.</param>
  /// <returns>A count per option id, in the poll's option order.</returns>
  public static IReadOnlyDictionary<Guid, int> Tally(Poll poll)
  {
    ArgumentNullException.ThrowIfNull(poll);
    var counts = new Dictionary<Guid, int>();
    foreach (var option in poll.Options)
    {
      counts[option.Id] = 0;
    }

    foreach (var vote in poll.Votes)
    {
      // A ballot is one voter: count each option at most once even if it appears twice in the payload.
      foreach (var optionId in vote.OptionIds.Distinct())
      {
        if (counts.TryGetValue(optionId, out var count))
        {
          counts[optionId] = count + 1;
        }
      }
    }

    return counts;
  }

  /// <summary>
  /// The share of voters an option got, rounded to a whole percent. The base is the number of voters,
  /// not of picks, so on a multi-choice poll the percentages can add up to more than 100.
  /// </summary>
  /// <param name="votes">The option's vote count.</param>
  /// <param name="voters">The number of users who voted.</param>
  /// <returns>The percentage, or 0 when nobody voted.</returns>
  public static int Percent(int votes, int voters)
    => voters <= 0 ? 0 : (int)Math.Round(votes * 100.0 / voters, MidpointRounding.AwayFromZero);

  /// <summary>
  /// Validates a ballot against the poll. Returns <c>null</c> when it is acceptable, otherwise one of
  /// the <c>Error*</c> codes.
  /// </summary>
  /// <param name="poll">The poll.</param>
  /// <param name="optionIds">The picked option ids.</param>
  /// <param name="utcNow">The current UTC time.</param>
  /// <returns>An error code, or <c>null</c> when the ballot is valid.</returns>
  public static string? ValidateVote(Poll poll, IReadOnlyCollection<Guid> optionIds, DateTime utcNow)
  {
    ArgumentNullException.ThrowIfNull(poll);
    ArgumentNullException.ThrowIfNull(optionIds);
    if (!IsOpen(poll, utcNow))
    {
      return ErrorClosed;
    }

    var picked = optionIds.Distinct().ToList();
    if (picked.Count == 0)
    {
      return ErrorEmpty;
    }

    if (!poll.MultiChoice && picked.Count > 1)
    {
      return ErrorSingleChoice;
    }

    return picked.Any(id => poll.Options.All(o => o.Id != id)) ? ErrorUnknownOption : null;
  }

  /// <summary>
  /// When the poll stopped accepting votes: the manual close, else its deadline, else its creation
  /// (a poll can only be closed after it was created, so that last fallback just keeps the date sane).
  /// </summary>
  /// <param name="poll">The poll.</param>
  /// <returns>The UTC time the poll closed.</returns>
  private static DateTime ClosedAt(Poll poll)
    => poll.ClosedAt ?? poll.ClosesAt ?? poll.CreatedAt;
}
