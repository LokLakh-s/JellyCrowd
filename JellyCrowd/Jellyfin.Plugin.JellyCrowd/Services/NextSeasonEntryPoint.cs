using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using JfEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Hands every episode a user starts to <see cref="INextSeasonRequester"/>, which requests the next
/// season when they near the end of the current one. Playback is never held up: the work runs in the
/// background and its failures are only logged.
/// </summary>
public sealed class NextSeasonEntryPoint : IHostedService
{
  private readonly ISessionManager _sessionManager;
  private readonly ILibraryManager _libraryManager;
  private readonly INextSeasonRequester _requester;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<NextSeasonEntryPoint> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="NextSeasonEntryPoint"/> class.
  /// </summary>
  /// <param name="sessionManager">The Jellyfin session manager (source of playback events).</param>
  /// <param name="libraryManager">The library manager (resolves the episode's show).</param>
  /// <param name="requester">Decides on and makes the request.</param>
  /// <param name="config">The plugin configuration accessor.</param>
  /// <param name="logger">The logger.</param>
  public NextSeasonEntryPoint(
    ISessionManager sessionManager,
    ILibraryManager libraryManager,
    INextSeasonRequester requester,
    Func<PluginConfiguration> config,
    ILogger<NextSeasonEntryPoint> logger)
  {
    _sessionManager = sessionManager;
    _libraryManager = libraryManager;
    _requester = requester;
    _config = config;
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

  /// <summary>
  /// Reads what the next-season request needs from a played item: its show's TMDB id and name, and its
  /// season and episode numbers. <c>null</c> for anything else — a movie, a pre-roll, a special, an episode
  /// without numbers, or a show TMDB does not identify.
  /// </summary>
  /// <param name="item">The played item.</param>
  /// <param name="libraryManager">The library manager (resolves the episode's show).</param>
  /// <returns>The episode's context, or <c>null</c>.</returns>
  internal static (int SeriesTmdbId, string SeriesName, int Season, int Episode)? ResolveEpisode(BaseItem? item, ILibraryManager libraryManager)
  {
    if (item is not JfEpisode episode || episode.ParentIndexNumber is not int season || season <= 0 || episode.IndexNumber is not int number)
    {
      return null;
    }

    var series = libraryManager.GetItemById(episode.SeriesId);
    var tmdb = series?.GetProviderId(MetadataProvider.Tmdb);
    if (string.IsNullOrEmpty(tmdb) || !int.TryParse(tmdb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
    {
      return null;
    }

    return (tmdbId, series!.Name ?? episode.SeriesName ?? string.Empty, season, number);
  }

  private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
  {
    if (!_config().AutoNextSeasonEnabled || e?.Session?.UserId is not { } userId || userId == Guid.Empty)
    {
      return;
    }

    if (ResolveEpisode(e.Item, _libraryManager) is { } played)
    {
      _ = ConsiderSafeAsync(userId, played.SeriesTmdbId, played.SeriesName, played.Season, played.Episode);
    }
  }

  private async Task ConsiderSafeAsync(Guid userId, int seriesTmdbId, string seriesName, int season, int episode)
  {
    try
    {
      var outcome = await _requester.ConsiderAsync(userId, seriesTmdbId, seriesName, season, episode, CancellationToken.None).ConfigureAwait(false);
      _logger.LogDebug("Next season of {Series} after S{Season}E{Episode}: {Outcome}", seriesName, season, episode, outcome);
    }
#pragma warning disable CA1031 // Best-effort background work: never let it surface to playback.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Could not consider the next season of {Series} for user {UserId}", seriesName, userId);
    }
  }
}
