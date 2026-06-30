using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using JfEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Captures playback history for the statistics screens: one record per completed viewing session (who
/// watched what, when, and for how long), built from Jellyfin's playback events. Independent of the
/// adaptive-quota entry point and gated by <see cref="PluginConfiguration.StatsEnabled"/>. Watched minutes
/// are a wall-clock estimate between progress pings, capped to ignore long pauses/gaps.
/// </summary>
public sealed class PlaybackHistoryEntryPoint : IHostedService
{
  private const double MinPlayMinutes = 1.0; // ignore accidental blips
  private static readonly TimeSpan MaxDelta = TimeSpan.FromMinutes(5);

  private readonly ISessionManager _sessionManager;
  private readonly IPlaybackHistoryStore _store;
  private readonly Func<Guid, string> _resolveUserName;
  private readonly Func<PluginConfiguration> _configurationProvider;
  private readonly ILogger<PlaybackHistoryEntryPoint> _logger;
  private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

  /// <summary>
  /// Initializes a new instance of the <see cref="PlaybackHistoryEntryPoint"/> class.
  /// </summary>
  /// <param name="sessionManager">The Jellyfin session manager (source of playback events).</param>
  /// <param name="store">The playback-history store.</param>
  /// <param name="resolveUserName">Resolves a user id to a display name.</param>
  /// <param name="configurationProvider">Provides the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public PlaybackHistoryEntryPoint(
    ISessionManager sessionManager,
    IPlaybackHistoryStore store,
    Func<Guid, string> resolveUserName,
    Func<PluginConfiguration> configurationProvider,
    ILogger<PlaybackHistoryEntryPoint> logger)
  {
    _sessionManager = sessionManager;
    _store = store;
    _resolveUserName = resolveUserName;
    _configurationProvider = configurationProvider;
    _logger = logger;
  }

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken)
  {
    _sessionManager.PlaybackStart += OnPlaybackStart;
    _sessionManager.PlaybackProgress += OnPlaybackProgress;
    _sessionManager.PlaybackStopped += OnPlaybackStopped;
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken)
  {
    _sessionManager.PlaybackStart -= OnPlaybackStart;
    _sessionManager.PlaybackProgress -= OnPlaybackProgress;
    _sessionManager.PlaybackStopped -= OnPlaybackStopped;
    return Task.CompletedTask;
  }

  private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
  {
    if (!_configurationProvider().StatsEnabled || e?.Session?.Id is not { } sessionId || e.Item is null)
    {
      return;
    }

    _sessions[sessionId] = Capture(e, DateTime.UtcNow);
  }

  private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
  {
    if (!_configurationProvider().StatsEnabled || e?.Session?.Id is not { } sessionId)
    {
      return;
    }

    var now = DateTime.UtcNow;
    if (!_sessions.TryGetValue(sessionId, out var state))
    {
      // Progress without a tracked start (e.g. already playing when the plugin loaded): start tracking now.
      if (e.Item is not null)
      {
        _sessions[sessionId] = Capture(e, now);
      }

      return;
    }

    if (!e.IsPaused)
    {
      AddDelta(state, now);
    }

    state.LastTickUtc = now;
  }

  private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
  {
    if (e?.Session?.Id is not { } sessionId || !_sessions.TryRemove(sessionId, out var state))
    {
      return;
    }

    var now = DateTime.UtcNow;
    if (!e.IsPaused)
    {
      AddDelta(state, now);
    }

    if (!_configurationProvider().StatsEnabled || state.Minutes < MinPlayMinutes)
    {
      return;
    }

    var record = new PlaybackRecord
    {
      UserId = state.UserId,
      UserName = state.UserName,
      ItemId = state.ItemId,
      ItemName = state.ItemName,
      ItemType = state.ItemType,
      SeriesName = state.SeriesName,
      SeriesId = state.SeriesId,
      Season = state.Season,
      Episode = state.Episode,
      Client = state.Client,
      PlayedAtUtc = state.StartUtc,
      Minutes = Math.Round(state.Minutes, 2)
    };

    _ = SaveSafeAsync(record);
  }

  private async Task SaveSafeAsync(PlaybackRecord record)
  {
    try
    {
      await _store.AddAsync(record, CancellationToken.None).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // Statistics capture is best-effort; never let it surface to playback.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Jelly Crowd: failed to store a playback record.");
    }
  }

  private SessionState Capture(PlaybackProgressEventArgs e, DateTime now)
  {
    var item = e.Item!;
    var userId = e.Session?.UserId ?? Guid.Empty;
    var state = new SessionState
    {
      UserId = userId,
      UserName = userId == Guid.Empty ? string.Empty : SafeName(userId),
      ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
      ItemName = item.Name ?? string.Empty,
      ItemType = ItemTypeOf(item),
      Client = e.Session?.Client ?? string.Empty,
      StartUtc = now,
      LastTickUtc = now,
      Minutes = 0
    };

    if (item is JfEpisode ep)
    {
      state.SeriesName = ep.SeriesName ?? string.Empty;
      state.SeriesId = ep.SeriesId.ToString("N", CultureInfo.InvariantCulture);
      state.Season = ep.ParentIndexNumber;
      state.Episode = ep.IndexNumber;
    }

    return state;
  }

  private string SafeName(Guid userId)
  {
    try
    {
      return _resolveUserName(userId) ?? string.Empty;
    }
#pragma warning disable CA1031 // Name resolution is best-effort.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Jelly Crowd: could not resolve a viewer name.");
      return string.Empty;
    }
  }

  private static void AddDelta(SessionState state, DateTime now)
  {
    var elapsed = now - state.LastTickUtc;
    if (elapsed <= TimeSpan.Zero)
    {
      return;
    }

    state.Minutes += (elapsed < MaxDelta ? elapsed : MaxDelta).TotalMinutes;
  }

  private static string ItemTypeOf(BaseItem item)
  {
    if (item is JfEpisode)
    {
      return "Episode";
    }

    return item is Movie ? "Movie" : item.GetType().Name;
  }

  private sealed class SessionState
  {
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string ItemType { get; set; } = string.Empty;

    public string SeriesName { get; set; } = string.Empty;

    public string SeriesId { get; set; } = string.Empty;

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public string Client { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }

    public DateTime LastTickUtc { get; set; }

    public double Minutes { get; set; }
  }
}
