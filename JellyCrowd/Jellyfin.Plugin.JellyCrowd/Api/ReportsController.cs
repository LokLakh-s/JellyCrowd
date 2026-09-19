using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// User-submitted issue reports about titles, and the admin triage queue.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/Reports")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public class ReportsController : ControllerBase
{
  private const int MaxMessageLength = 2000;

  // The media categories, then "account" for the reports that come with no title (access, sign-in…).
  // "other" catches anything unknown, so a client sending a category we do not know still files its report.
  private static readonly string[] KnownTypes = { "bug", "subtitles", "audio", "quality", "account", "other" };

  private readonly IReportStore _store;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly Func<Guid, string> _resolveUserName;
  private readonly IActivityLog _activityLog;
  private readonly INotificationService _notifications;

  /// <summary>
  /// Initializes a new instance of the <see cref="ReportsController"/> class.
  /// </summary>
  /// <param name="store">The report store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="resolveUserName">Resolves a user id to a display name.</param>
  /// <param name="activityLog">The activity log.</param>
  /// <param name="notifications">The notification service.</param>
  public ReportsController(IReportStore store, ICurrentUserAccessor userAccessor, Func<Guid, string> resolveUserName, IActivityLog activityLog, INotificationService notifications)
  {
    _store = store;
    _userAccessor = userAccessor;
    _resolveUserName = resolveUserName;
    _activityLog = activityLog;
    _notifications = notifications;
  }

  private static string NormalizeType(string? type)
  {
    foreach (var known in KnownTypes)
    {
      if (string.Equals(known, type, StringComparison.OrdinalIgnoreCase))
      {
        return known;
      }
    }

    return "other";
  }

  /// <summary>
  /// Submits an issue report. It concerns a title when the media fields are filled in (the catalogue's
  /// "Report a problem" button), otherwise it is a general one — the user reaching an administrator about
  /// something that is not a specific title.
  /// </summary>
  /// <param name="dto">The report payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The created report.</response>
  /// <response code="400">The payload was invalid.</response>
  /// <returns>The persisted report with its id and timestamp.</returns>
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<MediaReport>> Post([FromBody] ReportDto dto, CancellationToken cancellationToken)
  {
    if (dto is null || string.IsNullOrWhiteSpace(dto.Message))
    {
      return BadRequest("A message is required.");
    }

    // No media type at all = a general report, which carries no title. A media type that is set must
    // still be one we understand: a typo would silently file the ticket against nothing.
    var general = string.IsNullOrWhiteSpace(dto.MediaType);
    if (!general
        && !string.Equals(dto.MediaType, "movie", StringComparison.Ordinal)
        && !string.Equals(dto.MediaType, "tv", StringComparison.Ordinal))
    {
      return BadRequest("The 'mediaType' must be 'movie', 'tv', or empty for a general report.");
    }

    var message = dto.Message.Trim();
    if (message.Length > MaxMessageLength)
    {
      message = message[..MaxMessageLength];
    }

    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var created = await _store.AddAsync(
      new MediaReport
      {
        MediaType = general ? string.Empty : dto.MediaType,
        TmdbId = general ? 0 : dto.TmdbId,
        Title = general ? string.Empty : dto.Title,
        UserId = userId,
        UserName = _resolveUserName(userId),
        Message = message,
        Type = NormalizeType(dto.Type)
      },
      cancellationToken).ConfigureAwait(false);

    var subjectOf = ReportDigest.LogSubjectOf(created);
    await _activityLog.LogAsync(
      "info", "report", $"New report ({created.Type}): {subjectOf} — {created.UserName}", created.UserName, cancellationToken).ConfigureAwait(false);

    // Tell the administrators now: until this existed, a report only reached them if they happened to
    // open the moderation screen (and the user guide promised otherwise).
    var t = ServerStrings.For(Plugin.Instance?.Configuration?.Language);
    _ = _notifications.NotifyAdminsAsync(
      t("notif_report_new_subject"),
      ReportDigest.NewReportBody(created, t),
      CancellationToken.None);

    return Ok(created);
  }

  /// <summary>
  /// Lists all reports (administrators only).
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The reports, newest first.</response>
  /// <returns>The report queue.</returns>
  [HttpGet]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<MediaReport>>> GetAll(CancellationToken cancellationToken)
  {
    return Ok(await _store.GetAllAsync(cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Marks a report resolved (administrators only), optionally with a note that is sent to the reporter.
  /// </summary>
  /// <param name="id">The report id.</param>
  /// <param name="dto">Optional resolution payload (an admin note for the reporter).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The updated report.</response>
  /// <response code="404">No such report.</response>
  /// <returns>The report with its resolved flag set.</returns>
  [HttpPost("{id:guid}/Resolve")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<MediaReport>> Resolve(Guid id, [FromBody] ResolveReportDto? dto, CancellationToken cancellationToken)
  {
    var updated = await _store.SetResolvedAsync(id, resolved: true, dto?.Response, cancellationToken).ConfigureAwait(false);
    if (updated is null)
    {
      return NotFound();
    }

    await _activityLog.LogAsync("info", "report", $"Report resolved: {updated.Title}", updated.UserName, cancellationToken).ConfigureAwait(false);

    // Tell the reporter their ticket was handled (in-app bell always; personal channels are un-gated here).
    var t = ServerStrings.For(Plugin.Instance?.Configuration?.Language);
    var body = (string.IsNullOrWhiteSpace(updated.AdminResponse)
        ? t("notif_report_resolved_body")
        : t("notif_report_resolved_body_note").Replace("{note}", updated.AdminResponse, StringComparison.Ordinal))
      .Replace("{title}", updated.Title, StringComparison.Ordinal);
    _ = _notifications.NotifyPersonalAsync(
      updated.UserId, PersonalNotifyKind.None, updated.Title, t("notif_report_resolved_subject"), body, null, CancellationToken.None);

    return Ok(updated);
  }

  /// <summary>
  /// Deletes a report (administrators only).
  /// </summary>
  /// <param name="id">The report id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="204">The report was deleted.</response>
  /// <response code="404">No such report.</response>
  /// <returns>No content on success; 404 otherwise.</returns>
  [HttpPost("{id:guid}/Delete")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
  {
    return await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false) ? NoContent() : NotFound();
  }
}
