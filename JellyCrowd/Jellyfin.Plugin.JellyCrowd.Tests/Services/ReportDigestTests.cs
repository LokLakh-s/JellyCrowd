using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ReportDigest"/> — the wording admins receive and the rule that decides when the
/// reports left open deserve a reminder.
/// </summary>
public class ReportDigestTests
{
  private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

  // Echoes the key back with its placeholders, so a test asserts on the substitutions, not on wording.
  private static string T(string key) => key switch
  {
    "report_general" => "General",
    "report_type_audio" => "Audio",
    "report_type_account" => "Account",
    "notif_report_new_body" => "{user}|{type}|{subject}|{message}",
    "notif_report_reminder_body" => "{n}|{subject}|{days}",
    _ => key,
  };

  private static MediaReport Report(int ageDays, bool resolved = false, string title = "Dune")
    => new()
    {
      Title = title,
      MediaType = string.IsNullOrEmpty(title) ? string.Empty : "movie",
      UserName = "mel",
      Message = "no sound",
      Type = "audio",
      Resolved = resolved,
      CreatedAt = Now.AddDays(-ageDays)
    };

  [Fact]
  public void SubjectOf_FallsBackToTheGeneralLabel_WhenNoTitle()
  {
    Assert.Equal("Dune", ReportDigest.SubjectOf(Report(0), T));
    Assert.Equal("General", ReportDigest.SubjectOf(Report(0, title: string.Empty), T));
  }

  [Fact]
  public void LogSubjectOf_MarksATitlelessReport()
  {
    Assert.Equal("Dune", ReportDigest.LogSubjectOf(Report(0)));
    Assert.Equal("(general)", ReportDigest.LogSubjectOf(Report(0, title: string.Empty)));
  }

  [Fact]
  public void NewReportBody_CarriesWhoWhatAndTheMessage()
  {
    Assert.Equal("mel|Audio|Dune|no sound", ReportDigest.NewReportBody(Report(0), T));
  }

  [Fact]
  public void NewReportBody_UsesTheGeneralLabel_ForATitlelessReport()
  {
    var general = Report(0, title: string.Empty);
    general.Type = "account";

    Assert.Equal("mel|Account|General|no sound", ReportDigest.NewReportBody(general, T));
  }

  [Fact]
  public void PlanReminder_Disabled_NeverReminds()
  {
    var reports = new List<MediaReport> { Report(30) };

    Assert.Null(ReportDigest.PlanReminder(reports, Now, 0, null));
    Assert.Null(ReportDigest.PlanReminder(reports, Now, -1, null));
  }

  [Fact]
  public void PlanReminder_IgnoresReportsYoungerThanTheWindow()
  {
    // Filed two days ago with a three-day window: it was announced when it came in, nagging now would
    // teach admins to tune the reminder out.
    Assert.Null(ReportDigest.PlanReminder(new List<MediaReport> { Report(2) }, Now, 3, null));
  }

  [Fact]
  public void PlanReminder_IgnoresResolvedReports()
  {
    Assert.Null(ReportDigest.PlanReminder(new List<MediaReport> { Report(30, resolved: true) }, Now, 3, null));
  }

  [Fact]
  public void PlanReminder_CountsWhatIsWaiting_AndPicksTheOldest()
  {
    var reports = new List<MediaReport>
    {
      Report(4, title: "Recent"),
      Report(9, title: "Oldest"),
      Report(1, title: "Fresh"),             // younger than the window: not counted
      Report(20, resolved: true, title: "Done") // resolved: not counted
    };

    var plan = ReportDigest.PlanReminder(reports, Now, 3, null);

    Assert.NotNull(plan);
    Assert.Equal(2, plan!.OpenCount);
    Assert.Equal("Oldest", plan.Oldest.Title);
    Assert.Equal(9, plan.OldestAgeDays);
  }

  [Fact]
  public void PlanReminder_OnlyOneRecapPerWindow()
  {
    var reports = new List<MediaReport> { Report(30) };

    // Reminded yesterday, window of three days: too soon.
    Assert.Null(ReportDigest.PlanReminder(reports, Now, 3, Now.AddDays(-1)));
    // A full window has passed: remind again.
    Assert.NotNull(ReportDigest.PlanReminder(reports, Now, 3, Now.AddDays(-3)));
  }

  [Fact]
  public void ReminderBody_SaysHowManyAndHowLong()
  {
    var plan = ReportDigest.PlanReminder(new List<MediaReport> { Report(9), Report(5) }, Now, 3, null);

    Assert.Equal("2|Dune|9", ReportDigest.ReminderBody(plan!, T));
  }

  [Fact]
  public void PlanReminder_EmptyStore_Silent()
  {
    Assert.Null(ReportDigest.PlanReminder(Array.Empty<MediaReport>(), Now, 3, null));
  }
}
