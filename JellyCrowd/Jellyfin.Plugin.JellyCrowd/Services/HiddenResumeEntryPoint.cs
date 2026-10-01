using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using JfEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Puts a removed movie or show back into "Continue watching" and "Next up" as soon as the user plays it
/// again — removing it was about not being reminded, not about never watching it.
/// </summary>
public sealed class HiddenResumeEntryPoint : IHostedService
{
  private readonly ISessionManager _sessionManager;
  private readonly IHiddenResumeStore _store;
  private readonly ILogger<HiddenResumeEntryPoint> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="HiddenResumeEntryPoint"/> class.
  /// </summary>
  /// <param name="sessionManager">The Jellyfin session manager (source of playback events).</param>
  /// <param name="store">The removals store.</param>
  /// <param name="logger">The logger.</param>
  public HiddenResumeEntryPoint(ISessionManager sessionManager, IHiddenResumeStore store, ILogger<HiddenResumeEntryPoint> logger)
  {
    _sessionManager = sessionManager;
    _store = store;
    _logger = logger;
  }

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken)
  {
    _sessionManager.PlaybackStart += OnPlaybackStart;
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken)
  {
    _sessionManager.PlaybackStart -= OnPlaybackStart;
    return Task.CompletedTask;
  }

  private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
  {
    if (e?.Item is not { } item || e.Session?.UserId is not { } userId || userId == Guid.Empty)
    {
      return;
    }

    var seriesId = item is JfEpisode episode && episode.SeriesId != Guid.Empty ? episode.SeriesId : (Guid?)null;
    _ = UnhideSafeAsync(userId, item.Id, seriesId);
  }

  private async Task UnhideSafeAsync(Guid userId, Guid itemId, Guid? seriesId)
  {
    try
    {
      await _store.UnhideForPlaybackAsync(userId, itemId, seriesId, CancellationToken.None).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // Best-effort: never let it surface to playback.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Jelly Crowd: could not put item {ItemId} back into Continue watching.", itemId);
    }
  }
}
