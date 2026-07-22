using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Drives the adaptive quota from Jellyfin playback events. Records per-user watch minutes (a wall-clock
/// estimate between progress pings, capped to ignore long pauses/gaps) into <see cref="IUserActivityStore"/>,
/// and on a periodic timer runs the <see cref="AdaptiveQuotaCalculator"/> state machine to move users between
/// tiers and start/resolve probation, notifying them on meaningful changes. Inert while the feature is off.
/// </summary>
public sealed class PlaybackActivityEntryPoint : IHostedService, IDisposable
{
  private static readonly TimeSpan MaxDelta = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan EvaluateInterval = TimeSpan.FromHours(1);
  private static readonly TimeSpan EvaluateStartDelay = TimeSpan.FromMinutes(5);

  private readonly ISessionManager _sessionManager;
  private readonly IUserActivityStore _activityStore;
  private readonly IQuotaService _quotaService;
  private readonly INotificationService _notificationService;
  private readonly Func<PluginConfiguration> _configurationProvider;
  private readonly ILogger<PlaybackActivityEntryPoint> _logger;
  private readonly ConcurrentDictionary<string, DateTime> _lastTickBySession = new(StringComparer.Ordinal);
  private Timer? _timer;

  /// <summary>
  /// Initializes a new instance of the <see cref="PlaybackActivityEntryPoint"/> class.
  /// </summary>
  /// <param name="sessionManager">The Jellyfin session manager (source of playback events).</param>
  /// <param name="activityStore">The viewing-activity store.</param>
  /// <param name="quotaService">The quota service (for base quotas).</param>
  /// <param name="notificationService">The notification service.</param>
  /// <param name="configurationProvider">Provides the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public PlaybackActivityEntryPoint(
    ISessionManager sessionManager,
    IUserActivityStore activityStore,
    IQuotaService quotaService,
    INotificationService notificationService,
    Func<PluginConfiguration> configurationProvider,
    ILogger<PlaybackActivityEntryPoint> logger)
  {
    _sessionManager = sessionManager;
    _activityStore = activityStore;
    _quotaService = quotaService;
    _notificationService = notificationService;
    _configurationProvider = configurationProvider;
    _logger = logger;
  }

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken)
  {
    _sessionManager.PlaybackStart += OnPlaybackStart;
    _sessionManager.PlaybackProgress += OnPlaybackProgress;
    _sessionManager.PlaybackStopped += OnPlaybackStopped;
    _timer = new Timer(_ => _ = EvaluateSafeAsync(), null, EvaluateStartDelay, EvaluateInterval);
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken)
  {
    _sessionManager.PlaybackStart -= OnPlaybackStart;
    _sessionManager.PlaybackProgress -= OnPlaybackProgress;
    _sessionManager.PlaybackStopped -= OnPlaybackStopped;
    _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public void Dispose()
  {
    _timer?.Dispose();
    _timer = null;
  }

  private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
  {
    var sessionId = e?.Session?.Id;
    if (sessionId is not null)
    {
      _lastTickBySession[sessionId] = DateTime.UtcNow;
    }
  }

  private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
  {
    if (!_configurationProvider().AdaptiveQuotaEnabled || e?.Session?.Id is not { } sessionId)
    {
      return;
    }

    var now = DateTime.UtcNow;
    if (!e.IsPaused && _lastTickBySession.TryGetValue(sessionId, out var last))
    {
      RecordDelta(e, last, now);
    }

    _lastTickBySession[sessionId] = now;
  }

  private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
  {
    if (e?.Session?.Id is not { } sessionId)
    {
      return;
    }

    var now = DateTime.UtcNow;
    if (_lastTickBySession.TryRemove(sessionId, out var last) && _configurationProvider().AdaptiveQuotaEnabled && !e.IsPaused)
    {
      RecordDelta(e, last, now);
    }
  }

  private void RecordDelta(PlaybackProgressEventArgs e, DateTime last, DateTime now)
  {
    var elapsed = now - last;
    if (elapsed <= TimeSpan.Zero)
    {
      return;
    }

    var minutes = (elapsed < MaxDelta ? elapsed : MaxDelta).TotalMinutes;
    var userId = e.Session?.UserId ?? Guid.Empty;
    if (userId != Guid.Empty)
    {
      _activityStore.RecordPlayback(userId, now, minutes);
    }
  }

  private async Task EvaluateSafeAsync()
  {
    try
    {
      var config = _configurationProvider();
      if (!config.AdaptiveQuotaEnabled)
      {
        return;
      }

      var now = DateTime.UtcNow;
      foreach (var activity in _activityStore.GetAll())
      {
        var baseBytes = _quotaService.GetBaseQuotaBytes(activity.UserId);
        var transition = AdaptiveQuotaCalculator.Evaluate(config, activity, baseBytes, now);
        if (!transition.Changed)
        {
          continue;
        }

        activity.Tier = transition.Tier;
        activity.ProbationStartUtc = transition.ProbationStartUtc;
        activity.FrozenQuotaBytes = transition.FrozenQuotaBytes;
        _activityStore.Update(activity);
        await NotifyAsync(activity.UserId, transition.Event).ConfigureAwait(false);
      }
    }
#pragma warning disable CA1031 // A background evaluation failure must not crash the timer.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd adaptive-quota evaluation failed.");
    }
  }

  private Task NotifyAsync(Guid userId, AdaptiveEvent change)
  {
    var t = ServerStrings.For(Plugin.Instance?.Configuration?.Language);
    var key = change switch
    {
      AdaptiveEvent.Promoted => "notif_adaptive_promoted",
      AdaptiveEvent.ProbationStarted => "notif_adaptive_probation",
      AdaptiveEvent.ProbationPassed => "notif_adaptive_restored",
      AdaptiveEvent.ProbationFailed => "notif_adaptive_standard",
      _ => null,
    };

    if (key is null)
    {
      return Task.CompletedTask;
    }

    var subject = t(key + "_subject");
    var body = t(key + "_body");

    if (subject.Length == 0)
    {
      return Task.CompletedTask;
    }

    return _notificationService.NotifyPersonalAsync(
      userId,
      PersonalNotifyKind.QuotaExpiry,
      t("notif_adaptive_title"),
      subject,
      body,
      posterPath: null,
      CancellationToken.None);
  }
}
