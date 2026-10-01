using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="INextSeasonRequester"/>.
/// </summary>
public sealed class NextSeasonRequester : INextSeasonRequester
{
  private readonly Func<PluginConfiguration?> _config;
  private readonly IUserPrefsStore _prefs;
  private readonly ISeriesStructureProvider _structure;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly ITmdbClient _tmdbClient;
  private readonly IRequestCreationService _creator;
  private readonly IAutoRequestLedger _ledger;
  private readonly INotificationService _notifications;
  private readonly IActivityLog _activityLog;
  private readonly Func<Guid, string> _resolveUserName;
  private readonly Func<DateTime> _now;
  private readonly ILogger<NextSeasonRequester> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="NextSeasonRequester"/> class.
  /// </summary>
  /// <param name="config">The plugin configuration accessor; <c>null</c> while the plugin is not loaded.</param>
  /// <param name="prefs">The user preferences (the opt-in).</param>
  /// <param name="structure">The show structure the catalog offers (seasons and episodes, Sonarr-numbered when Sonarr is the backend).</param>
  /// <param name="libraryMatcher">The library matcher (whether the next season is already there).</param>
  /// <param name="tmdbClient">The TMDB client (the show's poster).</param>
  /// <param name="creator">Creates the request through the normal path.</param>
  /// <param name="ledger">Remembers the seasons already dealt with.</param>
  /// <param name="notifications">Tells the user what was requested for them.</param>
  /// <param name="activityLog">The activity log.</param>
  /// <param name="resolveUserName">Resolves a user id to a display name for the activity log.</param>
  /// <param name="logger">The logger.</param>
  /// <param name="now">Clock accessor (defaults to <see cref="DateTime.UtcNow"/>); injectable for tests.</param>
  public NextSeasonRequester(
    Func<PluginConfiguration?> config,
    IUserPrefsStore prefs,
    ISeriesStructureProvider structure,
    ILibraryMatcher libraryMatcher,
    ITmdbClient tmdbClient,
    IRequestCreationService creator,
    IAutoRequestLedger ledger,
    INotificationService notifications,
    IActivityLog activityLog,
    Func<Guid, string> resolveUserName,
    ILogger<NextSeasonRequester> logger,
    Func<DateTime>? now = null)
  {
    _config = config;
    _prefs = prefs;
    _structure = structure;
    _libraryMatcher = libraryMatcher;
    _tmdbClient = tmdbClient;
    _creator = creator;
    _ledger = ledger;
    _notifications = notifications;
    _activityLog = activityLog;
    _resolveUserName = resolveUserName;
    _logger = logger;
    _now = now ?? (() => DateTime.UtcNow);
  }

  /// <inheritdoc />
  public async Task<NextSeasonOutcome> ConsiderAsync(Guid userId, int seriesTmdbId, string seriesName, int season, int episode, CancellationToken cancellationToken)
  {
    var config = _config();
    if (config is null || !config.AutoNextSeasonEnabled)
    {
      return NextSeasonOutcome.Disabled;
    }

    if (!(await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false)).AutoRequestNextSeason)
    {
      return NextSeasonOutcome.NotOptedIn;
    }

    var seasons = await _structure.GetSeasonsAsync(seriesTmdbId, "en-US", cancellationToken).ConfigureAwait(false);
    if (NextSeasonPlanner.NextSeasonToRequest(seasons, season, episode, config.AutoNextSeasonEpisodesLeft) is not int next)
    {
      return NextSeasonOutcome.NotYet;
    }

    if (await _ledger.ContainsAsync(userId, seriesTmdbId, next, cancellationToken).ConfigureAwait(false))
    {
      return NextSeasonOutcome.AlreadyHandled;
    }

    // Already (even partly) in the library: someone fetched it, and the user can simply watch it. Requesting
    // it would only charge it to their quota — a rewatch of an old season must not claim the next ones.
    if (_libraryMatcher.FindEpisodeItemId(seriesTmdbId, next, null) is not null)
    {
      await _ledger.RecordAsync(userId, seriesTmdbId, next, cancellationToken).ConfigureAwait(false);
      return NextSeasonOutcome.AlreadyInLibrary;
    }

    var episodes = await _structure.GetEpisodesAsync(seriesTmdbId, next, "en-US", cancellationToken).ConfigureAwait(false);
    var poster = await FindPosterAsync(seriesTmdbId, cancellationToken).ConfigureAwait(false);

    var created = 0;
    var unavailable = false;
    foreach (var (number, airDate) in NextSeasonPlanner.PlanRequests(episodes, _now()))
    {
      var result = await _creator.CreateAsync(
        userId,
        new CreateRequestDto
        {
          TmdbId = seriesTmdbId,
          MediaType = "tv",
          Title = seriesName,
          PosterPath = poster,
          Season = next,
          Episode = number,
          ReleaseDate = airDate
        },
        cancellationToken).ConfigureAwait(false);
      if (result.Outcome == RequestCreationOutcome.Created)
      {
        created++;
      }
      else if (result.Outcome == RequestCreationOutcome.Unavailable)
      {
        unavailable = true;
      }
    }

    // Nothing recorded and a check could not be made: leave the season open, a later episode retries it.
    if (created == 0 && unavailable)
    {
      return NextSeasonOutcome.Unavailable;
    }

    await _ledger.RecordAsync(userId, seriesTmdbId, next, cancellationToken).ConfigureAwait(false);
    if (created == 0)
    {
      return NextSeasonOutcome.Refused;
    }

    Announce(userId, seriesName, poster, season, next);
    return NextSeasonOutcome.Requested;
  }

  // The show's poster for the request and its notifications; a missing one never blocks the request.
  private async Task<string?> FindPosterAsync(int seriesTmdbId, CancellationToken cancellationToken)
  {
    try
    {
      return (await _tmdbClient.GetDetailsAsync("tv", seriesTmdbId, "en-US", cancellationToken).ConfigureAwait(false))?.PosterPath;
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
    {
      _logger.LogDebug(ex, "No poster for show {TmdbId}", seriesTmdbId);
      return null;
    }
  }

  // Tells the user what was requested for them (bell, plus their channels if they opted into decisions),
  // and records it in the activity log.
  private void Announce(Guid userId, string seriesName, string? poster, int season, int next)
  {
    var strings = ServerStrings.For(_config()?.Language);
    var body = strings("notif_auto_season_body")
      .Replace("{title}", seriesName, StringComparison.Ordinal)
      .Replace("{season}", next.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
      .Replace("{previous}", season.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    _ = _notifications.NotifyPersonalAsync(userId, PersonalNotifyKind.Decision, seriesName, strings("notif_auto_season_subject"), body, poster, CancellationToken.None);

    var name = _resolveUserName(userId);
    _ = _activityLog.LogAsync(
      "info",
      "user",
      name + ": season " + next.ToString(CultureInfo.InvariantCulture) + " of " + seriesName + " requested automatically",
      name,
      CancellationToken.None);
  }
}
