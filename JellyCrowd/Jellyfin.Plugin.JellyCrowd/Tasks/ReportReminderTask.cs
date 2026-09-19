using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Tasks;

/// <summary>
/// Scheduled task that reminds administrators of the reports left open. It runs often; whether anything
/// is actually sent is decided by <see cref="ReportDigest.PlanReminder"/>, which holds the window rule.
/// </summary>
public sealed class ReportReminderTask : IScheduledTask
{
  private readonly IReportStore _store;
  private readonly INotificationService _notificationService;
  private readonly Func<PluginConfiguration> _configurationProvider;
  private readonly Action<PluginConfiguration> _saveConfiguration;
  private readonly ILogger<ReportReminderTask> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="ReportReminderTask"/> class.
  /// </summary>
  /// <param name="store">The report store.</param>
  /// <param name="notificationService">The notification service (administrator fan-out).</param>
  /// <param name="configurationProvider">Provides the current plugin configuration.</param>
  /// <param name="saveConfiguration">Persists the configuration (the last-reminder stamp lives there).</param>
  /// <param name="logger">The logger.</param>
  public ReportReminderTask(
    IReportStore store,
    INotificationService notificationService,
    Func<PluginConfiguration> configurationProvider,
    Action<PluginConfiguration> saveConfiguration,
    ILogger<ReportReminderTask> logger)
  {
    _store = store;
    _notificationService = notificationService;
    _configurationProvider = configurationProvider;
    _saveConfiguration = saveConfiguration;
    _logger = logger;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd: remind about open reports";

  /// <inheritdoc />
  public string Key => "JellyCrowdReportReminder";

  /// <inheritdoc />
  public string Description => "Sends administrators a recap of the user reports that are still waiting.";

  /// <inheritdoc />
  public string Category => "Jelly Crowd";

  /// <inheritdoc />
  public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(progress);
    progress.Report(0);

    var config = _configurationProvider();
    var reports = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var reminder = ReportDigest.PlanReminder(reports, DateTime.UtcNow, config.ReportReminderDays, config.LastReportReminderUtc);
    if (reminder is null)
    {
      progress.Report(100);
      return;
    }

    var t = ServerStrings.For(config.Language);
    await _notificationService.NotifyAdminsAsync(t("notif_report_reminder_subject"), ReportDigest.ReminderBody(reminder, t), cancellationToken).ConfigureAwait(false);

    // Stamp only after the fan-out: a delivery that throws (it should not — the service swallows) must
    // not silently consume this window's reminder.
    config.LastReportReminderUtc = DateTime.UtcNow;
    _saveConfiguration(config);
    _logger.LogInformation("Jelly Crowd reports: reminded about {Count} open report(s).", reminder.OpenCount);
    progress.Report(100);
  }

  /// <inheritdoc />
  public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
  {
    // Hourly: the window is counted in days, so this only has to be fine enough that a reminder goes out
    // the same day it comes due.
    return new[]
    {
      new TaskTriggerInfo
      {
        Type = TaskTriggerInfoType.IntervalTrigger,
        IntervalTicks = TimeSpan.FromHours(1).Ticks
      }
    };
  }
}
