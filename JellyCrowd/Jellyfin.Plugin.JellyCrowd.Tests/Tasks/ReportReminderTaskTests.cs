using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Jellyfin.Plugin.JellyCrowd.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Tasks;

/// <summary>
/// Tests for <see cref="ReportReminderTask"/>: the glue between the store, the window rule and the
/// administrator fan-out (the rule itself is covered by <c>ReportDigestTests</c>).
/// </summary>
public sealed class ReportReminderTaskTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonReportStore _store;
  private readonly RecordingNotificationService _notifications = new();
  private readonly PluginConfiguration _config = new() { ReportReminderDays = 3 };
  private int _saves;

  public ReportReminderTaskTests() => _store = new JsonReportStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private ReportReminderTask CreateTask()
    => new(_store, _notifications, () => _config, _ => _saves++, NullLogger<ReportReminderTask>.Instance);

  private Task<MediaReport> AddReportAsync(int ageDays, bool resolved = false)
    => _store.AddAsync(
      new MediaReport
      {
        MediaType = "movie",
        TmdbId = 1,
        Title = "Dune",
        UserName = "mel",
        Message = "no sound",
        Resolved = resolved,
        CreatedAt = DateTime.UtcNow.AddDays(-ageDays)
      },
      CancellationToken.None);

  [Fact]
  public async Task Execute_OpenReportPastTheWindow_NotifiesAndStamps()
  {
    await AddReportAsync(5);

    await CreateTask().ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Single(_notifications.AdminNotices);
    Assert.NotNull(_config.LastReportReminderUtc);
    Assert.Equal(1, _saves);
  }

  [Fact]
  public async Task Execute_NothingWaiting_StaysSilent()
  {
    await AddReportAsync(1);              // too young
    await AddReportAsync(30, resolved: true); // already handled

    await CreateTask().ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Empty(_notifications.AdminNotices);
    Assert.Null(_config.LastReportReminderUtc);
    Assert.Equal(0, _saves);
  }

  [Fact]
  public async Task Execute_TwiceInTheSameWindow_RemindsOnce()
  {
    await AddReportAsync(5);
    var task = CreateTask();

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);
    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Single(_notifications.AdminNotices);
  }

  [Fact]
  public async Task Execute_RemindersDisabled_StaysSilent()
  {
    _config.ReportReminderDays = 0;
    await AddReportAsync(90);

    await CreateTask().ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Empty(_notifications.AdminNotices);
  }
}
