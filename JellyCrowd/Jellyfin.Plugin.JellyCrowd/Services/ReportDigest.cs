using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Turns reports into the text administrators actually receive, and decides when the ones left open
/// deserve a reminder. Pure, so the wording and the "should we nag" rule are unit-tested without a
/// notification channel or a clock.
/// </summary>
public static class ReportDigest
{
  /// <summary>
  /// What a report is about, for the activity log: the title it names, or a marker for the reports that
  /// carry none. Not translated — the log is server-side and read by whoever runs the server.
  /// </summary>
  /// <param name="report">The report.</param>
  /// <returns>The log subject.</returns>
  public static string LogSubjectOf(MediaReport report)
  {
    ArgumentNullException.ThrowIfNull(report);
    return string.IsNullOrWhiteSpace(report.Title) ? "(general)" : report.Title;
  }

  /// <summary>
  /// What a report is about, for a message a human reads: the title it names, or the localized label for
  /// a report that stands on its own.
  /// </summary>
  /// <param name="report">The report.</param>
  /// <param name="t">The translation lookup.</param>
  /// <returns>The subject line fragment.</returns>
  public static string SubjectOf(MediaReport report, Func<string, string> t)
  {
    ArgumentNullException.ThrowIfNull(report);
    ArgumentNullException.ThrowIfNull(t);
    return string.IsNullOrWhiteSpace(report.Title) ? t("report_general") : report.Title;
  }

  /// <summary>
  /// The body of the "a report just came in" notice: who, which category, about what, and what they said.
  /// </summary>
  /// <param name="report">The report just filed.</param>
  /// <param name="t">The translation lookup.</param>
  /// <returns>The message body.</returns>
  public static string NewReportBody(MediaReport report, Func<string, string> t)
  {
    ArgumentNullException.ThrowIfNull(report);
    ArgumentNullException.ThrowIfNull(t);
    return t("notif_report_new_body")
      .Replace("{user}", report.UserName, StringComparison.Ordinal)
      .Replace("{type}", t("report_type_" + report.Type), StringComparison.Ordinal)
      .Replace("{subject}", SubjectOf(report, t), StringComparison.Ordinal)
      .Replace("{message}", report.Message, StringComparison.Ordinal);
  }

  /// <summary>
  /// Decides whether administrators should be reminded of the reports still open, and what the reminder
  /// covers. Returns <c>null</c> when there is nothing to say.
  /// <para>
  /// A report counts only once it has been waiting a whole window: one opened minutes ago was already
  /// announced, and nagging about it immediately would teach admins to ignore the reminder. At most one
  /// recap per window goes out, however many reports are waiting — a backlog must not become a flood.
  /// </para>
  /// </summary>
  /// <param name="reports">Every stored report, resolved ones included.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="reminderDays">The configured window in days; 0 or less disables reminders.</param>
  /// <param name="lastReminderUtc">When the previous recap was sent, or <c>null</c> if never.</param>
  /// <returns>The reminder to send, or <c>null</c>.</returns>
  public static ReportReminder? PlanReminder(IReadOnlyList<MediaReport> reports, DateTime nowUtc, int reminderDays, DateTime? lastReminderUtc)
  {
    if (reports is null || reports.Count == 0 || reminderDays <= 0)
    {
      return null;
    }

    var window = TimeSpan.FromDays(reminderDays);
    var waiting = reports
      .Where(r => !r.Resolved && nowUtc - r.CreatedAt >= window)
      .OrderBy(r => r.CreatedAt)
      .ToList();
    if (waiting.Count == 0)
    {
      return null;
    }

    if (lastReminderUtc is { } last && nowUtc - last < window)
    {
      return null;
    }

    var oldest = waiting[0];
    return new ReportReminder(waiting.Count, oldest, (int)Math.Floor((nowUtc - oldest.CreatedAt).TotalDays));
  }

  /// <summary>
  /// The body of a reminder: how many are waiting, and how long the oldest has been.
  /// </summary>
  /// <param name="reminder">The planned reminder.</param>
  /// <param name="t">The translation lookup.</param>
  /// <returns>The message body.</returns>
  public static string ReminderBody(ReportReminder reminder, Func<string, string> t)
  {
    ArgumentNullException.ThrowIfNull(reminder);
    ArgumentNullException.ThrowIfNull(t);
    return t("notif_report_reminder_body")
      .Replace("{n}", reminder.OpenCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
      .Replace("{subject}", SubjectOf(reminder.Oldest, t), StringComparison.Ordinal)
      .Replace("{days}", reminder.OldestAgeDays.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
  }
}
