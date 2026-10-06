using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Download backend that adds approved requests to Radarr (movies) or Sonarr (shows) and triggers a
/// search. Shows are resolved from TMDB to TVDB first, since Sonarr is TVDB-based. Jelly Crowd only
/// registers the item; Radarr/Sonarr perform the actual search and download.
/// </summary>
public sealed class ServarrDownloadClient : IDownloadClient
{
  // TMDB episode lists are read in one language everywhere, so the season checks share the cached lists.
  private const string TmdbEpisodeLanguage = "en-US";

  // A fresh Sonarr add processes monitoring asynchronously: because we add inert (monitor: none), Sonarr
  // unmonitors everything a moment after the add returns, which would clobber a single up-front monitor
  // call and leave the series unmonitored (nothing searchable). After applying monitoring we therefore
  // wait, re-check, and re-apply until Sonarr has finished that step and our monitoring held — bounded by
  // these constants.
  private const int MonitorConfirmAttempts = 10;
  private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1);

  private readonly IServarrClient _servarr;
  private readonly ITmdbClient _tmdb;
  private readonly Func<PluginConfiguration> _config;
  private readonly Func<CancellationToken, Task> _settleDelay;
  private readonly IServarrProfileResolver? _profiles;

  /// <summary>
  /// Initializes a new instance of the <see cref="ServarrDownloadClient"/> class.
  /// </summary>
  /// <param name="servarr">The Radarr/Sonarr client.</param>
  /// <param name="tmdb">The TMDB client (TMDB-&gt;TVDB resolution for shows).</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="settleDelay">
  /// Delay awaited between monitor-confirm attempts, letting Sonarr's asynchronous post-add processing run.
  /// Defaults to a one-second real delay; tests inject a no-op.
  /// </param>
  /// <param name="profiles">Picks each title's quality profile from its requesters' language preferences; <c>null</c> keeps the configured profiles.</param>
  public ServarrDownloadClient(IServarrClient servarr, ITmdbClient tmdb, Func<PluginConfiguration> config, Func<CancellationToken, Task>? settleDelay = null, IServarrProfileResolver? profiles = null)
  {
    _servarr = servarr;
    _tmdb = tmdb;
    _config = config;
    _settleDelay = settleDelay ?? (ct => Task.Delay(SettleDelay, ct));
    _profiles = profiles;
  }

  /// <inheritdoc />
  public string Backend => "servarr";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return RadarrConfigured(config) || SonarrConfigured(config);
  }

  /// <inheritdoc />
  public Task DispatchAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    return EnsureRequestedAsync(dispatch, cancellationToken);
  }

  /// <inheritdoc />
  public Task RetryAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    return EnsureRequestedAsync(dispatch, cancellationToken);
  }

  /// <inheritdoc />
  public async Task RescanAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    var config = _config();

    if (string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal))
    {
      if (!RadarrConfigured(config))
      {
        return;
      }

      var movie = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      if (movie?["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var movieId) && movieId > 0)
      {
        var command = new JsonObject { ["name"] = "RescanMovie", ["movieId"] = movieId };
        await _servarr.CommandAsync(config.RadarrUrl, config.RadarrApiKey, command, cancellationToken).ConfigureAwait(false);
      }
    }
    else if (string.Equals(dispatch.MediaType, "tv", StringComparison.Ordinal))
    {
      if (!SonarrConfigured(config))
      {
        return;
      }

      var tvdbId = await ResolveTvdbIdAsync(config, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      if (tvdbId is null)
      {
        return;
      }

      var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false);
      if (series is not null && series["id"] is JsonValue seriesIdValue && seriesIdValue.TryGetValue<int>(out var seriesId) && seriesId > 0)
      {
        var command = new JsonObject { ["name"] = "RescanSeries", ["seriesId"] = seriesId };
        await _servarr.CommandAsync(config.SonarrUrl, config.SonarrApiKey, command, cancellationToken).ConfigureAwait(false);
      }
    }
  }

  // Idempotent dispatch: if the title is already in Radarr/Sonarr, (re)trigger a search; otherwise add
  // it (the add payload already requests a search). Crucially, re-adding an existing title makes
  // Radarr/Sonarr return 400 — which previously failed the dispatch, left it "due", and made the
  // scheduled task re-add it forever (the "Blocked / 400 Bad Request" flapping). Checking first avoids that.
  private async Task EnsureRequestedAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    var config = _config();

    if (string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal))
    {
      if (!RadarrConfigured(config))
      {
        throw new InvalidOperationException("Radarr is not configured (URL, API key, root folder and quality profile are required).");
      }

      var profile = await ResolveProfileAsync("movie", dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      var movie = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      if (TryGetId(movie, out var movieId))
      {
        // Already in Radarr — just (re)search it instead of re-adding (which would 400). A requester with
        // another preference may have changed the profile it is due: switch it first, so the search goes
        // after what everyone asked for.
        if (SwitchProfile(movie!, profile))
        {
          await _servarr.UpdateMovieAsync(config.RadarrUrl, config.RadarrApiKey, movieId, movie!, cancellationToken).ConfigureAwait(false);
        }

        await SearchMovieAsync(config, movieId, cancellationToken).ConfigureAwait(false);
        return;
      }

      var lookup = await _servarr.LookupMovieAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Radarr could not find TMDB movie {dispatch.TmdbId.ToString(CultureInfo.InvariantCulture)}.");
      var body = ServarrPayload.BuildMovieAdd(lookup, profile ?? config.RadarrQualityProfileId, config.RadarrRootFolderPath);
      try
      {
        await _servarr.AddMovieAsync(config.RadarrUrl, config.RadarrApiKey, body, cancellationToken).ConfigureAwait(false);
      }
      catch (HttpRequestException)
      {
        // A concurrent request (e.g. several titles grabbed at once) may have added it first, so this add
        // 400s ("movie already exists"). If it's there now, recover by searching it instead of failing the
        // dispatch — a failed dispatch would surface a spurious "Blocked" until reconciliation.
        var added = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
        if (!TryGetId(added, out var addedMovieId))
        {
          throw;
        }

        await SearchMovieAsync(config, addedMovieId, cancellationToken).ConfigureAwait(false);
      }
    }
    else if (string.Equals(dispatch.MediaType, "tv", StringComparison.Ordinal))
    {
      if (!SonarrConfigured(config))
      {
        throw new InvalidOperationException("Sonarr is not configured (URL, API key, root folder and quality profile are required).");
      }

      var tvdbId = await ResolveTvdbIdAsync(config, dispatch.TmdbId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(
          $"No TVDB series found for TMDB show {dispatch.TmdbId.ToString(CultureInfo.InvariantCulture)} (\"{dispatch.Title}\"): neither TMDB nor Sonarr links them (TVDB, IMDb or TMDB id). It may be a season of another series there: add it in Sonarr by hand.");
      var profile = await ResolveProfileAsync("tv", dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false);
      if (series is not null && TryGetId(series, out var seriesId))
      {
        // TMDB's seasons must be Sonarr's seasons, or the wrong one would be downloaded.
        if (await MisalignmentAsync(config, dispatch, series, seriesId, cancellationToken).ConfigureAwait(false) is { } misaligned)
        {
          throw new InvalidOperationException(misaligned);
        }

        // One profile per show in Sonarr: a requester of another season with another preference can change it.
        if (SwitchProfile(series, profile))
        {
          await _servarr.UpdateSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, series, cancellationToken).ConfigureAwait(false);
        }

        await SearchSeriesAsync(config, series, seriesId, dispatch, cancellationToken).ConfigureAwait(false);
        return;
      }

      var lookup = await _servarr.LookupSeriesAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Sonarr could not find TVDB series {tvdbId.ToString(CultureInfo.InvariantCulture)}.");

      // Before adding anything: a TVDB series that started long before (or after) the TMDB show is not that
      // show — TMDB's season 1 is a later season there.
      if (await StartMismatchAsync(dispatch, lookup, cancellationToken).ConfigureAwait(false) is { } apart)
      {
        throw new InvalidOperationException(apart);
      }

      var body = ServarrPayload.BuildSeriesAdd(lookup, profile ?? config.SonarrQualityProfileId, config.SonarrLanguageProfileId, config.SonarrRootFolderPath, dispatch.Season);
      var addedHere = true;
      try
      {
        await _servarr.AddSeriesAsync(config.SonarrUrl, config.SonarrApiKey, body, cancellationToken).ConfigureAwait(false);
      }
      catch (HttpRequestException)
      {
        addedHere = false;
        // A concurrent request (e.g. two seasons of the same show grabbed at once) may have added the
        // series first, so this add 400s ("series already added"). That is fine — it is in Sonarr now, and
        // the monitor + search below handles it. Any other add failure is surfaced by the fetch that follows.
      }

      // The series was added inert (nothing monitored, no search). Fetch it and monitor + search ONLY the
      // requested season — never rely on the add to grab, or Sonarr's default "monitor: all" pulls the
      // whole show for a single-season request.
      var added = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false);
      if (added is null || !TryGetId(added, out var addedSeriesId))
      {
        throw new InvalidOperationException($"Sonarr did not accept the series for TVDB {tvdbId.ToString(CultureInfo.InvariantCulture)}.");
      }

      // Fresh add: confirm the monitoring sticks against Sonarr's async post-add unmonitor before searching.
      await ConfirmMonitorThenSearchSeriesAsync(config, added, addedSeriesId, tvdbId, dispatch, addedHere, cancellationToken).ConfigureAwait(false);
    }
    else
    {
      throw new InvalidOperationException($"Unsupported media type '{dispatch.MediaType}'.");
    }
  }

  // The quality profile the title is due from its requesters' language preferences, or null when they have
  // no say (no resolver, feature off, nobody with a preference, or the lookup failed): a new title then gets
  // the configured profile and an existing one keeps whatever it has — including a profile set by hand.
  private async Task<int?> ResolveProfileAsync(string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    if (_profiles is null)
    {
      return null;
    }

    try
    {
      var resolved = await _profiles.ResolveAsync(mediaType, tmdbId, cancellationToken).ConfigureAwait(false);
      return resolved > 0 ? resolved : null;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      return null;
    }
  }

  // Sets the profile on a Radarr movie / Sonarr series body; whether it changed (and so must be saved).
  private static bool SwitchProfile(JsonObject item, int? profile)
  {
    if (profile is not int target || target <= 0
        || (item["qualityProfileId"] is JsonValue current && current.TryGetValue<int>(out var id) && id == target))
    {
      return false;
    }

    item["qualityProfileId"] = target;
    return true;
  }

  // (Re)search a movie already present in Radarr.
  private Task SearchMovieAsync(PluginConfiguration config, int movieId, CancellationToken cancellationToken)
  {
    var command = new JsonObject { ["name"] = "MoviesSearch", ["movieIds"] = new JsonArray(movieId) };
    return _servarr.CommandAsync(config.RadarrUrl, config.RadarrApiKey, command, cancellationToken);
  }

  // Used when the series is ALREADY in Sonarr: there is no async post-add processing to race, so a single
  // application of the monitoring sticks. Then search the requested scope.
  private async Task SearchSeriesAsync(PluginConfiguration config, JsonObject series, int seriesId, DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    var episodesJson = await ApplyMonitoringAsync(config, series, seriesId, dispatch, cancellationToken).ConfigureAwait(false);
    await SearchScopeAsync(config, seriesId, dispatch, episodesJson, cancellationToken).ConfigureAwait(false);
  }

  // Used right after a FRESH add: Sonarr processes the add asynchronously and — because we add inert
  // (monitor: none) — unmonitors the series' seasons and episodes a moment later, which clobbers monitoring
  // applied before it runs (the reported "series stays unmonitored, monitor/search greyed out" bug). So we
  // apply the monitoring, wait, re-check, and re-apply until Sonarr has finished its post-add (it then clears
  // the series' add options) AND our monitoring survived it (bounded), and only then search — so the search
  // actually has monitored episodes.
  // Once Sonarr has listed the episodes, their air dates are checked against TMDB's before any search: on a
  // mismatch, a series added for this request alone is removed again (nothing was searched, nothing grabbed).
  private async Task ConfirmMonitorThenSearchSeriesAsync(PluginConfiguration config, JsonObject series, int seriesId, int tvdbId, DownloadDispatch dispatch, bool addedHere, CancellationToken cancellationToken)
  {
    var current = series;
    string? episodesJson = null;
    for (var attempt = 0; attempt < MonitorConfirmAttempts; attempt++)
    {
      episodesJson = await ApplyMonitoringAsync(config, current, seriesId, dispatch, cancellationToken).ConfigureAwait(false);

      await _settleDelay(cancellationToken).ConfigureAwait(false);

      var refreshed = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId, cancellationToken).ConfigureAwait(false);
      if (refreshed is null)
      {
        break; // can't verify — we already applied the monitoring; fall through to the search.
      }

      current = refreshed;
      if (ServarrPayload.IsPostAddComplete(current)
          && await IsMonitoringInPlaceAsync(config, current, seriesId, dispatch, cancellationToken).ConfigureAwait(false))
      {
        break; // Sonarr's post-add has run and our monitoring survived it — stable.
      }
    }

    if (await MisalignmentAsync(config, dispatch, current, seriesId, cancellationToken).ConfigureAwait(false) is { } misaligned)
    {
      if (addedHere)
      {
        await _servarr.DeleteSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, deleteFiles: false, cancellationToken).ConfigureAwait(false);
      }

      throw new InvalidOperationException(misaligned);
    }

    await SearchScopeAsync(config, seriesId, dispatch, episodesJson, cancellationToken).ConfigureAwait(false);
  }

  // Puts Sonarr's monitoring in line with the request — never wider than it. A single episode monitors that
  // episode alone (plus the series flag, without which Sonarr grabs nothing) and leaves the season flag as
  // it is: turning a season on makes Sonarr monitor every episode of it, and every one it lists later, which
  // pulled whole seasons for single-episode requests. A season, or the whole series, turns its season flags
  // on (a series added for an earlier season leaves later ones unmonitored) and monitors their episodes one
  // by one (Sonarr's inert add and any prior deletion leave them unmonitored, and the season flag alone does
  // not reliably cascade back). Returns the series' episode list as read, for the search.
  private async Task<string?> ApplyMonitoringAsync(PluginConfiguration config, JsonObject series, int seriesId, DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    var changed = IsEpisodeScope(dispatch)
      ? ServarrPayload.EnsureSeriesMonitored(series)
      : ServarrPayload.EnsureSeasonsMonitored(series, dispatch.Season);

    // A whole-series request also wants the seasons Sonarr lists later.
    if (dispatch.Season is null && ServarrPayload.SetFollowsNewSeasons(series, follow: true))
    {
      changed = true;
    }

    if (changed)
    {
      await _servarr.UpdateSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, series, cancellationToken).ConfigureAwait(false);
    }

    var episodesJson = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
    if (!string.IsNullOrEmpty(episodesJson))
    {
      var episodeIds = ServarrEpisodeParser.EpisodeIdsToMonitor(episodesJson, dispatch.Season, dispatch.Episode);
      if (episodeIds.Count > 0)
      {
        await _servarr.SetEpisodesMonitoredAsync(config.SonarrUrl, config.SonarrApiKey, episodeIds, monitored: true, cancellationToken).ConfigureAwait(false);
      }
    }

    return episodesJson;
  }

  // Whether the request's monitoring is in place: the series and the requested season(s), or — for a single
  // episode — the series and that episode (an episode Sonarr does not list yet has nothing to check).
  private async Task<bool> IsMonitoringInPlaceAsync(PluginConfiguration config, JsonObject series, int seriesId, DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    if (!IsEpisodeScope(dispatch))
    {
      return ServarrPayload.AreSeasonsMonitored(series, dispatch.Season);
    }

    if (series["monitored"] is not JsonValue rootValue || !rootValue.TryGetValue<bool>(out var rootMonitored) || !rootMonitored)
    {
      return false;
    }

    var episodesJson = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
    var listed = string.IsNullOrEmpty(episodesJson) ? null : ServarrEpisodeParser.FindEpisode(episodesJson, dispatch.Season!.Value, dispatch.Episode!.Value);
    return listed is not { Monitored: false };
  }

  // Searches the requested scope: one episode on its own (Sonarr's episode search, which ignores season
  // packs), a season, or the whole series. An episode Sonarr does not list yet is not searched: the
  // request's next retry finds it once Sonarr has it.
  private async Task SearchScopeAsync(PluginConfiguration config, int seriesId, DownloadDispatch dispatch, string? episodesJson, CancellationToken cancellationToken)
  {
    JsonObject command;
    if (IsEpisodeScope(dispatch))
    {
      var listed = string.IsNullOrEmpty(episodesJson) ? null : ServarrEpisodeParser.FindEpisode(episodesJson, dispatch.Season!.Value, dispatch.Episode!.Value);
      if (listed is not { } episode)
      {
        return;
      }

      command = new JsonObject { ["name"] = "EpisodeSearch", ["episodeIds"] = new JsonArray(episode.Id) };
    }
    else
    {
      command = dispatch.Season is int season
        ? new JsonObject { ["name"] = "SeasonSearch", ["seriesId"] = seriesId, ["seasonNumber"] = season }
        : new JsonObject { ["name"] = "SeriesSearch", ["seriesId"] = seriesId };
    }

    await _servarr.CommandAsync(config.SonarrUrl, config.SonarrApiKey, command, cancellationToken).ConfigureAwait(false);
  }

  private static bool IsEpisodeScope(DownloadDispatch dispatch) => dispatch.Season is not null && dispatch.Episode is not null;

  // Resolves the TVDB id Sonarr needs from a TMDB show id. Normally TMDB carries it, but some entries
  // (e.g. The Haunting of Hill House) have no TVDB id — for those we fall back to the IMDb id, which
  // Sonarr can look up, and whose result carries the TVDB id; then to Sonarr's own link to the TMDB id
  // (Monster: The Lizzie Borden Story has neither id). Returns null when no path resolves. A series found
  // this way is still checked season by season before anything is monitored (see MisalignmentAsync).
  private async Task<int?> ResolveTvdbIdAsync(PluginConfiguration config, int tmdbId, CancellationToken cancellationToken)
  {
    var tvdbId = await _tmdb.GetTvdbIdAsync(tmdbId, cancellationToken).ConfigureAwait(false);
    if (tvdbId is not null)
    {
      return tvdbId;
    }

    var details = await _tmdb.GetDetailsAsync("tv", tmdbId, "en-US", cancellationToken).ConfigureAwait(false);
    if (!string.IsNullOrEmpty(details?.ImdbId))
    {
      var byImdb = await _servarr.LookupSeriesByImdbAsync(config.SonarrUrl, config.SonarrApiKey, details.ImdbId, cancellationToken).ConfigureAwait(false);
      if (TvdbIdOf(byImdb) is int found)
      {
        return found;
      }
    }

    var byTmdb = await _servarr.LookupSeriesByTmdbAsync(config.SonarrUrl, config.SonarrApiKey, tmdbId, cancellationToken).ConfigureAwait(false);
    return TvdbIdOf(byTmdb);
  }

  private static int? TvdbIdOf(JsonObject? lookup)
    => lookup?["tvdbId"] is JsonValue v && v.TryGetValue<int>(out var resolved) && resolved > 0 ? resolved : null;

  // Why the request's seasons are not this Sonarr series' seasons, or null when they are (or nothing tells):
  // each TMDB season's air dates against Sonarr's. The whole show checks every season TMDB lists.
  private async Task<string?> MisalignmentAsync(PluginConfiguration config, DownloadDispatch dispatch, JsonObject series, int seriesId, CancellationToken cancellationToken)
  {
    IReadOnlyList<int> seasons;
    try
    {
      seasons = dispatch.Season is int requested
        ? new[] { requested }
        : (await _tmdb.GetSeasonsAsync(dispatch.TmdbId, TmdbEpisodeLanguage, cancellationToken).ConfigureAwait(false) ?? Array.Empty<Season>())
          .Select(s => s.SeasonNumber).Where(n => n > 0).ToList();
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      return null; // TMDB unreachable: the numbering is trusted, as before.
    }

    // TMDB's air dates first: with none to compare (seasons not dated yet), Sonarr need not be asked.
    var tmdbDates = new Dictionary<int, IReadOnlyList<DateTime>>();
    foreach (var season in seasons)
    {
      try
      {
        var episodes = await _tmdb.GetSeasonEpisodesAsync(dispatch.TmdbId, season, TmdbEpisodeLanguage, cancellationToken).ConfigureAwait(false);
        var dates = (episodes ?? Array.Empty<Episode>()).Select(e => SeasonAlignment.ParseDay(e.AirDate)).OfType<DateTime>().ToList();
        if (dates.Count > 0)
        {
          tmdbDates[season] = dates;
        }
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // This season stays unchecked.
      }
    }

    if (tmdbDates.Count == 0)
    {
      return null;
    }

    var episodesJson = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
    var sonarrDates = SeasonAlignment.ParseSonarrAirDates(episodesJson);
    foreach (var (season, dates) in tmdbDates)
    {
      var (verdict, elsewhere) = SeasonAlignment.Check(season, dates, sonarrDates);
      if (verdict == SeasonAlignment.Verdict.Mismatch)
      {
        var name = SeriesTitle(series);
        var number = season.ToString(CultureInfo.InvariantCulture);
        return elsewhere is int other
          ? $"TMDB season {number} of \"{dispatch.Title}\" is season {other.ToString(CultureInfo.InvariantCulture)} of \"{name}\" on TVDB, which Sonarr follows. Jelly Crowd does not map seasons between them: add that season in Sonarr by hand."
          : $"TMDB season {number} of \"{dispatch.Title}\" is not season {number} of \"{name}\" on TVDB, which Sonarr follows (other air dates). Jelly Crowd does not map seasons between them: add it in Sonarr by hand.";
      }
    }

    return null;
  }

  // Why a TVDB series about to be added is not the TMDB show (it started more than a month apart), or null.
  private async Task<string?> StartMismatchAsync(DownloadDispatch dispatch, JsonObject lookup, CancellationToken cancellationToken)
  {
    string? tmdbStart;
    try
    {
      tmdbStart = (await _tmdb.GetDetailsAsync("tv", dispatch.TmdbId, TmdbEpisodeLanguage, cancellationToken).ConfigureAwait(false))?.ReleaseDate;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      return null;
    }

    var tvdbStart = lookup["firstAired"] is JsonValue first && first.TryGetValue<string>(out var text) ? text : null;
    if (SeasonAlignment.StartTogether(tmdbStart, tvdbStart))
    {
      return null;
    }

    return $"\"{dispatch.Title}\" on TMDB (first aired {Day(tmdbStart)}) does not start with \"{SeriesTitle(lookup)}\" on TVDB, which Sonarr follows (first aired {Day(tvdbStart)}): it is likely a season of it there. Jelly Crowd does not map seasons between them: add it in Sonarr by hand.";
  }

  private static string SeriesTitle(JsonObject series)
    => series["title"] is JsonValue title && title.TryGetValue<string>(out var text) ? text : "?";

  private static string Day(string? date)
    => SeasonAlignment.ParseDay(date)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "?";

  private static bool TryGetId(JsonObject? obj, out int id)
  {
    if (obj?["id"] is JsonValue value && value.TryGetValue<int>(out var parsed) && parsed > 0)
    {
      id = parsed;
      return true;
    }

    id = 0;
    return false;
  }

  /// <inheritdoc />
  public Task CancelAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    var config = _config();

    // A movie leaves Radarr (which stops its search and download) unless another request still wants it.
    if (string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal))
    {
      return RadarrConfigured(config) && dispatch.KeepScopes.Count == 0
        ? RemoveMovieAsync(config, dispatch.TmdbId, deleteFiles: true, cancellationToken)
        : Task.CompletedTask;
    }

    // A show stays in Sonarr, files and all: what nobody else wants just stops being monitored and leaves
    // the queue, so Sonarr no longer downloads it for a request that is gone.
    return string.Equals(dispatch.MediaType, "tv", StringComparison.Ordinal) && SonarrConfigured(config)
      ? ReleaseAsync(config, dispatch, deleteFiles: false, cancellationToken)
      : Task.CompletedTask;
  }

  /// <inheritdoc />
  public async Task<bool> PurgeAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    var config = _config();

    try
    {
      if (string.Equals(dispatch.MediaType, "movie", StringComparison.Ordinal))
      {
        if (RadarrConfigured(config))
        {
          await RemoveMovieAsync(config, dispatch.TmdbId, deleteFiles: !dispatch.KeepFiles, cancellationToken).ConfigureAwait(false);
        }

        return true; // removed, or Radarr not configured (nothing this backend put there).
      }

      if (!string.Equals(dispatch.MediaType, "tv", StringComparison.Ordinal) || !SonarrConfigured(config))
      {
        return true;
      }

      // Part of the scope is still wanted by another request: withdraw only the rest.
      if (dispatch.KeepScopes.Any(k => MediaScope.Overlaps(dispatch.Season, dispatch.Episode, k.Season, k.Episode)))
      {
        await ReleaseAsync(config, dispatch, deleteFiles: true, cancellationToken).ConfigureAwait(false);
        return true;
      }

      var tvdbId = await ResolveTvdbIdAsync(config, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
      if (tvdbId is null)
      {
        return true; // can't resolve it → nothing actionable to purge.
      }

      var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false);
      if (series is null || series["id"] is not JsonValue idValue || !idValue.TryGetValue<int>(out var seriesId) || seriesId <= 0)
      {
        return true; // not in Sonarr → nothing to purge.
      }

      // Seasons that are not TMDB's own there were never sent for this request: nothing of it to purge.
      if (await MisalignmentAsync(config, dispatch, series, seriesId, cancellationToken).ConfigureAwait(false) is not null)
      {
        return true;
      }

      // Remove the active downloads of the purged scope from the client (best-effort).
      await RemoveSeriesQueueAsync(config, tvdbId.Value, dispatch.Season, dispatch.Episode, cancellationToken).ConfigureAwait(false);

      if (dispatch.Season is int season)
      {
        // Targeted purge: a single season (or one episode) — unmonitor it so Sonarr won't re-grab, then
        // delete just that season's/episode's files from Sonarr + disk. The whole series is left in place.
        var episodesJson = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
        var (episodeIds, fileIds) = ServarrEpisodeParser.Select(episodesJson, season, dispatch.Episode);

        if (dispatch.Episode is null)
        {
          if (ServarrPayload.UnmonitorSeason(series, season))
          {
            await _servarr.UpdateSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, series, cancellationToken).ConfigureAwait(false);
          }
        }
        else if (episodeIds.Count > 0)
        {
          await _servarr.SetEpisodesMonitoredAsync(config.SonarrUrl, config.SonarrApiKey, episodeIds, monitored: false, cancellationToken).ConfigureAwait(false);
        }

        foreach (var fileId in fileIds)
        {
          await _servarr.DeleteEpisodeFileAsync(config.SonarrUrl, config.SonarrApiKey, fileId, cancellationToken).ConfigureAwait(false);
        }

        return true;
      }

      // Whole-show request: remove the entire series (with its files, unless Jellyfin deletes them).
      await _servarr.DeleteSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, deleteFiles: !dispatch.KeepFiles, cancellationToken).ConfigureAwait(false);
      return true;
    }
#pragma warning disable CA1031 // A purge failure (e.g. backend down) is reported so the caller can retry.
    catch (Exception)
#pragma warning restore CA1031
    {
      return false;
    }
  }

  // Withdraws a request's scope from Sonarr without touching what the title's other requests still want (see
  // SonarrReleasePlan): season flags off where nothing holds them, the episodes they still want monitored
  // again after Sonarr's cascade, the released episodes unmonitored, their downloads out of the queue, and —
  // on deletion — their files removed.
  private async Task ReleaseAsync(PluginConfiguration config, DownloadDispatch dispatch, bool deleteFiles, CancellationToken cancellationToken)
  {
    var tvdbId = await ResolveTvdbIdAsync(config, dispatch.TmdbId, cancellationToken).ConfigureAwait(false);
    if (tvdbId is null)
    {
      return;
    }

    var series = await _servarr.GetSeriesByTvdbAsync(config.SonarrUrl, config.SonarrApiKey, tvdbId.Value, cancellationToken).ConfigureAwait(false);
    if (series is null || !TryGetId(series, out var seriesId))
    {
      return;
    }

    // Seasons that are not TMDB's own there (another show's, a season added by hand) were never this request's.
    if (await MisalignmentAsync(config, dispatch, series, seriesId, cancellationToken).ConfigureAwait(false) is not null)
    {
      return;
    }

    var episodesJson = await _servarr.GetEpisodesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, cancellationToken).ConfigureAwait(false);
    var episodes = string.IsNullOrEmpty(episodesJson) ? Array.Empty<SonarrEpisode>() : ServarrEpisodeParser.ParseEpisodes(episodesJson);
    var plan = SonarrReleasePlan.Build(dispatch.Season, dispatch.Episode, dispatch.KeepScopes, ServarrPayload.MonitoredSeasons(series), episodes);

    var seriesChanged = false;
    foreach (var season in plan.SeasonsToTurnOff)
    {
      seriesChanged |= ServarrPayload.UnmonitorSeason(series, season);
    }

    if (plan.StopFollowingNewSeasons)
    {
      seriesChanged |= ServarrPayload.SetFollowsNewSeasons(series, follow: false);
    }

    if (seriesChanged)
    {
      await _servarr.UpdateSeriesAsync(config.SonarrUrl, config.SonarrApiKey, seriesId, series, cancellationToken).ConfigureAwait(false);
    }

    if (plan.EpisodesToRemonitor.Count > 0)
    {
      await _servarr.SetEpisodesMonitoredAsync(config.SonarrUrl, config.SonarrApiKey, plan.EpisodesToRemonitor, monitored: true, cancellationToken).ConfigureAwait(false);
    }

    if (plan.EpisodesToUnmonitor.Count > 0)
    {
      await _servarr.SetEpisodesMonitoredAsync(config.SonarrUrl, config.SonarrApiKey, plan.EpisodesToUnmonitor, monitored: false, cancellationToken).ConfigureAwait(false);
    }

    if (plan.Released.Count > 0)
    {
      try
      {
        var queueJson = await _servarr.GetQueueAsync(config.SonarrUrl, config.SonarrApiKey, forSonarr: true, cancellationToken).ConfigureAwait(false);
        foreach (var queueId in ServarrQueueParser.ParseSeriesDownloadsWithin(queueJson, tvdbId.Value, plan.Released))
        {
          await _servarr.DeleteQueueItemAsync(config.SonarrUrl, config.SonarrApiKey, queueId, removeFromClient: true, blocklist: false, cancellationToken).ConfigureAwait(false);
        }
      }
#pragma warning disable CA1031 // Queue cleanup is best-effort; the monitoring above already stops new grabs.
      catch (Exception)
#pragma warning restore CA1031
      {
        // Ignore — the files (on deletion) are still removed below.
      }
    }

    if (deleteFiles)
    {
      foreach (var fileId in plan.FilesToDelete)
      {
        await _servarr.DeleteEpisodeFileAsync(config.SonarrUrl, config.SonarrApiKey, fileId, cancellationToken).ConfigureAwait(false);
      }
    }
  }

  private async Task RemoveMovieAsync(PluginConfiguration config, int tmdbId, bool deleteFiles, CancellationToken cancellationToken)
  {
    // First remove any active download from the download client — deleting the Radarr movie alone
    // leaves the grab running (e.g. the torrent/RDT job keeps going). Best-effort; never blocks the delete.
    try
    {
      var queueJson = await _servarr.GetQueueAsync(config.RadarrUrl, config.RadarrApiKey, forSonarr: false, cancellationToken).ConfigureAwait(false);
      foreach (var queueId in ServarrQueueParser.ParseMovieQueueRecordIds(queueJson, tmdbId))
      {
        await _servarr.DeleteQueueItemAsync(config.RadarrUrl, config.RadarrApiKey, queueId, removeFromClient: true, blocklist: false, cancellationToken).ConfigureAwait(false);
      }
    }
#pragma warning disable CA1031 // Queue cleanup is best-effort; still delete the movie below.
    catch (Exception)
#pragma warning restore CA1031
    {
      // Ignore — fall through to deleting the movie.
    }

    var movie = await _servarr.GetMovieByTmdbAsync(config.RadarrUrl, config.RadarrApiKey, tmdbId, cancellationToken).ConfigureAwait(false);
    if (movie?["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var movieId) && movieId > 0)
    {
      await _servarr.DeleteMovieAsync(config.RadarrUrl, config.RadarrApiKey, movieId, deleteFiles, cancellationToken).ConfigureAwait(false);
    }
  }

  // Only the downloads lying entirely within the purged scope: deleting one episode or season must not cancel
  // the downloads other requests of the same series are waiting for.
  private async Task RemoveSeriesQueueAsync(PluginConfiguration config, int tvdbId, int? season, int? episode, CancellationToken cancellationToken)
  {
    try
    {
      var queueJson = await _servarr.GetQueueAsync(config.SonarrUrl, config.SonarrApiKey, forSonarr: true, cancellationToken).ConfigureAwait(false);
      foreach (var queueId in ServarrQueueParser.ParseSeriesDownloadsWithin(queueJson, tvdbId, season, episode))
      {
        await _servarr.DeleteQueueItemAsync(config.SonarrUrl, config.SonarrApiKey, queueId, removeFromClient: true, blocklist: false, cancellationToken).ConfigureAwait(false);
      }
    }
#pragma warning disable CA1031 // Queue cleanup is best-effort; the season/series removal still proceeds.
    catch (Exception)
#pragma warning restore CA1031
    {
      // Ignore — fall through to the deletion.
    }
  }

  /// <inheritdoc />
  public async Task TestAsync(CancellationToken cancellationToken)
  {
    var config = _config();
    var tested = false;

    if (RadarrConfigured(config))
    {
      await _servarr.TestAsync(config.RadarrUrl, config.RadarrApiKey, cancellationToken).ConfigureAwait(false);
      tested = true;
    }

    if (SonarrConfigured(config))
    {
      await _servarr.TestAsync(config.SonarrUrl, config.SonarrApiKey, cancellationToken).ConfigureAwait(false);
      tested = true;
    }

    if (!tested)
    {
      throw new InvalidOperationException("Configure Radarr and/or Sonarr (URL, API key, root folder and quality profile).");
    }
  }

  private static bool RadarrConfigured(PluginConfiguration config)
    => !string.IsNullOrWhiteSpace(config.RadarrUrl)
       && !string.IsNullOrWhiteSpace(config.RadarrApiKey)
       && !string.IsNullOrWhiteSpace(config.RadarrRootFolderPath)
       && config.RadarrQualityProfileId > 0;

  private static bool SonarrConfigured(PluginConfiguration config)
    => !string.IsNullOrWhiteSpace(config.SonarrUrl)
       && !string.IsNullOrWhiteSpace(config.SonarrApiKey)
       && !string.IsNullOrWhiteSpace(config.SonarrRootFolderPath)
       && config.SonarrQualityProfileId > 0;
}
