using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Polls published with the announcements: what the current user may answer, and the admin endpoints
/// that create, close and delete them.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/Polls")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class PollsController : ControllerBase
{
  private const int MaxQuestionLength = 300;
  private const int MaxOptionLength = 120;
  private const int MaxOptions = 10;
  private const int MinOptions = 2;

  private readonly IPollStore _store;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly IUserNotificationStore _notifications;
  private readonly IActivityLog _activityLog;
  private readonly IUserManager _userManager;
  private readonly Func<Guid, string> _resolveUserName;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="PollsController"/> class.
  /// </summary>
  /// <param name="store">The poll store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="notifications">The per-user notification feed (the bell).</param>
  /// <param name="activityLog">The activity log (records the admin actions).</param>
  /// <param name="userManager">The Jellyfin user manager (resolves a poll's audience).</param>
  /// <param name="resolveUserName">Resolves a user id to a display name.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  public PollsController(
    IPollStore store,
    ICurrentUserAccessor userAccessor,
    IUserNotificationStore notifications,
    IActivityLog activityLog,
    IUserManager userManager,
    Func<Guid, string> resolveUserName,
    Func<PluginConfiguration> config)
  {
    _store = store;
    _userAccessor = userAccessor;
    _notifications = notifications;
    _activityLog = activityLog;
    _userManager = userManager;
    _resolveUserName = resolveUserName;
    _config = config;
  }

  /// <summary>
  /// Gets the polls the caller should see: the open ones addressed to them, and recently closed ones
  /// whose result they may read. Administrators get every poll.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The caller's polls, newest first.</response>
  /// <returns>The polls, resolved for the caller.</returns>
  [HttpGet("Mine")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<PollViewDto>>> Mine(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var isAdmin = await _userAccessor.IsAdministratorAsync(Request).ConfigureAwait(false);
    var now = DateTime.UtcNow;
    var config = _config();

    var polls = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var result = polls
      .Where(p => PollPolicy.ShouldSee(config, p, userId, isAdmin, now))
      .Select(p => ToView(p, config, userId, isAdmin, now))
      .ToList();

    return Ok(result);
  }

  /// <summary>
  /// Casts (or changes) the caller's vote. Re-posting replaces their previous answer while the poll is open.
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="dto">The picked option ids.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The poll, with the caller's vote applied.</response>
  /// <response code="400">The ballot was empty, picked several options on a single-choice poll, or named an unknown option.</response>
  /// <response code="403">The poll is not addressed to the caller.</response>
  /// <response code="404">No such poll.</response>
  /// <response code="409">The poll no longer accepts votes.</response>
  /// <returns>The updated poll as the caller sees it.</returns>
  [HttpPost("{id:guid}/Vote")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status403Forbidden)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<PollViewDto>> Vote(Guid id, [FromBody] PollVoteDto dto, CancellationToken cancellationToken)
  {
    var poll = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    if (poll is null)
    {
      return NotFound();
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var isAdmin = await _userAccessor.IsAdministratorAsync(Request).ConfigureAwait(false);
    var now = DateTime.UtcNow;
    var config = _config();

    // The audience decides who votes — an administrator outside it may watch the poll, not answer it.
    if (!RequestPolicy.IsInAudience(config, poll.GroupIds, userId))
    {
      return StatusCode(StatusCodes.Status403Forbidden, "This poll is not addressed to you.");
    }

    var picked = dto?.OptionIds ?? new Collection<Guid>();
    var error = PollPolicy.ValidateVote(poll, picked, now);
    if (error == PollPolicy.ErrorClosed)
    {
      return Conflict("This poll is closed.");
    }

    if (error is not null)
    {
      return BadRequest(error);
    }

    var vote = new PollVote { UserId = userId, UserName = _resolveUserName(userId) };
    foreach (var optionId in picked.Distinct())
    {
      vote.OptionIds.Add(optionId);
    }

    var updated = await _store.VoteAsync(id, vote, cancellationToken).ConfigureAwait(false);
    if (updated is null)
    {
      return NotFound();
    }

    return Ok(ToView(updated, config, userId, isAdmin, now));
  }

  /// <summary>
  /// Gets every poll with its full result — vote counts, voter names and who has not answered yet
  /// (administrators only).
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The polls, newest first.</response>
  /// <returns>The polls with their results.</returns>
  [HttpGet("All")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<PollAdminDto>>> GetAll(CancellationToken cancellationToken)
  {
    var now = DateTime.UtcNow;
    var polls = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    return Ok(polls.Select(p => ToAdmin(p, now)).ToList());
  }

  /// <summary>
  /// Publishes a poll and drops a notification in the bell of everyone it is addressed to
  /// (administrators only).
  /// </summary>
  /// <param name="dto">The poll to create.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created poll.</response>
  /// <response code="400">The question or the options were invalid.</response>
  /// <returns>The created poll with its (empty) result.</returns>
  [HttpPost]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<PollAdminDto>> Create([FromBody] PollDto dto, CancellationToken cancellationToken)
  {
    var poll = BuildPoll(dto, out var error);
    if (poll is null)
    {
      return BadRequest(error);
    }

    var created = await _store.AddAsync(poll, cancellationToken).ConfigureAwait(false);
    var adminId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    await NotifyAudienceAsync(created, adminId, cancellationToken).ConfigureAwait(false);

    _ = _activityLog.LogAsync("info", "admin", _resolveUserName(adminId) + " published the poll \"" + created.Question + "\"", _resolveUserName(adminId), CancellationToken.None);
    return Ok(ToAdmin(created, DateTime.UtcNow));
  }

  /// <summary>
  /// Edits a poll that nobody has voted in yet (administrators only).
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="dto">The new content.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The updated poll.</response>
  /// <response code="400">The question or the options were invalid.</response>
  /// <response code="404">No such poll.</response>
  /// <response code="409">The poll already has votes and can no longer be edited.</response>
  /// <returns>The poll with its new content.</returns>
  [HttpPost("{id:guid}")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<PollAdminDto>> Update(Guid id, [FromBody] PollDto dto, CancellationToken cancellationToken)
  {
    var existing = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    if (existing is null)
    {
      return NotFound();
    }

    if (existing.Votes.Count > 0)
    {
      return Conflict("This poll already has votes and can no longer be edited.");
    }

    var poll = BuildPoll(dto, out var error);
    if (poll is null)
    {
      return BadRequest(error);
    }

    var updated = await _store.UpdateAsync(id, poll, cancellationToken).ConfigureAwait(false);
    return updated is null ? NotFound() : Ok(ToAdmin(updated, DateTime.UtcNow));
  }

  /// <summary>
  /// Closes a poll: it stops accepting votes and its result becomes readable (administrators only).
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The closed poll.</response>
  /// <response code="404">No such poll.</response>
  /// <returns>The poll, no longer accepting votes.</returns>
  [HttpPost("{id:guid}/Close")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<PollAdminDto>> Close(Guid id, CancellationToken cancellationToken)
    => await SetClosedAsync(id, closed: true, cancellationToken).ConfigureAwait(false);

  /// <summary>
  /// Re-opens a closed poll (administrators only). A deadline that has already passed is dropped, since
  /// it would close the poll again at once.
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The re-opened poll.</response>
  /// <response code="404">No such poll.</response>
  /// <returns>The poll, accepting votes again.</returns>
  [HttpPost("{id:guid}/Reopen")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<PollAdminDto>> Reopen(Guid id, CancellationToken cancellationToken)
    => await SetClosedAsync(id, closed: false, cancellationToken).ConfigureAwait(false);

  /// <summary>
  /// Deletes a poll and the votes cast in it (administrators only).
  /// </summary>
  /// <param name="id">The poll id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The poll was deleted.</response>
  /// <response code="404">No such poll.</response>
  /// <returns>No content on success; 404 otherwise.</returns>
  [HttpPost("{id:guid}/Delete")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
  {
    var poll = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    if (poll is null || !await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false))
    {
      return NotFound();
    }

    var adminId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    _ = _activityLog.LogAsync("info", "admin", _resolveUserName(adminId) + " deleted the poll \"" + poll.Question + "\"", _resolveUserName(adminId), CancellationToken.None);
    return NoContent();
  }

  private async Task<ActionResult<PollAdminDto>> SetClosedAsync(Guid id, bool closed, CancellationToken cancellationToken)
  {
    var updated = await _store.SetClosedAsync(id, closed, cancellationToken).ConfigureAwait(false);
    if (updated is null)
    {
      return NotFound();
    }

    var adminId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var what = closed ? " closed the poll \"" : " re-opened the poll \"";
    _ = _activityLog.LogAsync("info", "admin", _resolveUserName(adminId) + what + updated.Question + "\"", _resolveUserName(adminId), CancellationToken.None);
    return Ok(ToAdmin(updated, DateTime.UtcNow));
  }

  // Validates the payload and turns it into a storable poll; returns null (with a reason) when invalid.
  private static Poll? BuildPoll(PollDto? dto, out string error)
  {
    error = string.Empty;
    var question = (dto?.Question ?? string.Empty).Trim();
    if (question.Length == 0)
    {
      error = "A question is required.";
      return null;
    }

    if (question.Length > MaxQuestionLength)
    {
      question = question[..MaxQuestionLength];
    }

    var labels = (dto?.Options ?? new Collection<string>())
      .Select(o => (o ?? string.Empty).Trim())
      .Where(o => o.Length > 0)
      .Select(o => o.Length > MaxOptionLength ? o[..MaxOptionLength] : o)
      .ToList();
    if (labels.Count < MinOptions)
    {
      error = "A poll needs at least " + MinOptions.ToString(CultureInfo.InvariantCulture) + " options.";
      return null;
    }

    if (labels.Count > MaxOptions)
    {
      error = "A poll takes at most " + MaxOptions.ToString(CultureInfo.InvariantCulture) + " options.";
      return null;
    }

    var poll = new Poll
    {
      Question = question,
      MultiChoice = dto!.MultiChoice,
      ShowResults = dto.ShowResults,
      // A deadline already in the past would publish a poll nobody can answer — treat it as "no deadline".
      ClosesAt = dto.ClosesAt is not null && dto.ClosesAt > DateTime.UtcNow ? dto.ClosesAt : null
    };

    foreach (var label in labels)
    {
      poll.Options.Add(new PollOption { Id = Guid.NewGuid(), Text = label });
    }

    foreach (var groupId in dto.GroupIds)
    {
      if (groupId != Guid.Empty && !poll.GroupIds.Contains(groupId))
      {
        poll.GroupIds.Add(groupId);
      }
    }

    return poll;
  }

  // Drops the poll in the bell of everyone it is addressed to, except its author (who just wrote it).
  private async Task NotifyAudienceAsync(Poll poll, Guid authorId, CancellationToken cancellationToken)
  {
    var t = ServerStrings.For(_config().Language);
    foreach (var (userId, _) in Audience(poll))
    {
      if (userId == authorId)
      {
        continue;
      }

      await _notifications.AddAsync(
        new UserNotification
        {
          UserId = userId,
          Event = "Poll",
          Title = poll.Question,
          Message = t("poll_notification_body"),
          RefId = poll.Id
        },
        cancellationToken).ConfigureAwait(false);
    }
  }

  // The users a poll is addressed to (its groups, or everyone when it is global), minus child accounts.
  private List<(Guid UserId, string UserName)> Audience(Poll poll)
  {
    var config = _config();
    var audience = new List<(Guid UserId, string UserName)>();
    foreach (var user in _userManager.GetUsers())
    {
      if (RequestPolicy.IsInAudience(config, poll.GroupIds, user.Id))
      {
        audience.Add((user.Id, user.Username));
      }
    }

    return audience;
  }

  private static PollViewDto ToView(Poll poll, PluginConfiguration config, Guid userId, bool isAdmin, DateTime now)
  {
    var showResults = PollPolicy.ResultsVisibleTo(poll, userId, isAdmin, now);
    var tally = PollPolicy.Tally(poll);
    var mine = poll.Votes.FirstOrDefault(v => v.UserId == userId);

    var view = new PollViewDto
    {
      Id = poll.Id,
      Question = poll.Question,
      MultiChoice = poll.MultiChoice,
      ClosesAt = poll.ClosesAt,
      Open = PollPolicy.IsOpen(poll, now),
      CanVote = PollPolicy.CanVote(config, poll, userId, now),
      Voted = mine is not null,
      ResultsVisible = showResults,
      TotalVotes = showResults ? poll.Votes.Count : 0
    };

    foreach (var option in poll.Options)
    {
      var votes = showResults ? tally[option.Id] : 0;
      view.Options.Add(new PollOptionViewDto
      {
        Id = option.Id,
        Text = option.Text,
        Votes = votes,
        Percent = showResults ? PollPolicy.Percent(votes, poll.Votes.Count) : 0
      });
    }

    if (mine is not null)
    {
      foreach (var optionId in mine.OptionIds)
      {
        view.MyOptionIds.Add(optionId);
      }
    }

    return view;
  }

  private PollAdminDto ToAdmin(Poll poll, DateTime now)
  {
    var tally = PollPolicy.Tally(poll);
    var dto = new PollAdminDto
    {
      Id = poll.Id,
      Question = poll.Question,
      MultiChoice = poll.MultiChoice,
      ShowResults = poll.ShowResults,
      CreatedAt = poll.CreatedAt,
      ClosesAt = poll.ClosesAt,
      Open = PollPolicy.IsOpen(poll, now),
      Closed = poll.Closed,
      TotalVotes = poll.Votes.Count,
      Editable = poll.Votes.Count == 0
    };

    foreach (var groupId in poll.GroupIds)
    {
      dto.GroupIds.Add(groupId);
    }

    foreach (var option in poll.Options)
    {
      var view = new PollOptionViewDto
      {
        Id = option.Id,
        Text = option.Text,
        Votes = tally[option.Id],
        Percent = PollPolicy.Percent(tally[option.Id], poll.Votes.Count)
      };
      foreach (var voter in poll.Votes.Where(v => v.OptionIds.Contains(option.Id)).OrderBy(v => v.UserName, StringComparer.OrdinalIgnoreCase))
      {
        view.Voters.Add(voter.UserName);
      }

      dto.Options.Add(view);
    }

    // Who is still expected to answer — the point of nominative votes is chasing the stragglers.
    foreach (var (userId, userName) in Audience(poll).OrderBy(a => a.UserName, StringComparer.OrdinalIgnoreCase))
    {
      if (poll.Votes.All(v => v.UserId != userId))
      {
        dto.NotVoted.Add(userName);
      }
    }

    return dto;
  }
}
