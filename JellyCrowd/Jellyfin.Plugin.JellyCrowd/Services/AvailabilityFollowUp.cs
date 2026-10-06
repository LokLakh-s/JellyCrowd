using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IAvailabilityFollowUp"/>. Per requester and title (a season counts as one): fetches
/// each missing subtitle language through Jellyfin's subtitle providers, then compares the file's tracks
/// with the requester's preferred version and sends one short notice when it is not met.
/// </summary>
public sealed class AvailabilityFollowUp : IAvailabilityFollowUp
{
  // A whole series can be many files; past this, the rest waits for the next availability.
  private const int MaxVideosPerTitle = 60;

  // Jellyfin probes a new file's tracks after adding it: give it time before reading them.
  private static readonly TimeSpan DefaultDelay = TimeSpan.FromMinutes(3);

  private readonly Func<PluginConfiguration?> _config;
  private readonly IUserPrefsStore _prefs;
  private readonly ILibraryMatcher _matcher;
  private readonly ILibraryManager _library;
  private readonly ISubtitleManager _subtitles;
  private readonly LanguageCodes _codes;
  private readonly ITmdbClient _tmdb;
  private readonly INotificationService _notifications;
  private readonly ILogger<AvailabilityFollowUp> _logger;
  private readonly Func<BaseItem, IReadOnlyList<MediaStream>> _streams;
  private readonly TimeSpan _delay;

  /// <summary>
  /// Initializes a new instance of the <see cref="AvailabilityFollowUp"/> class.
  /// </summary>
  /// <param name="config">The plugin configuration accessor; <c>null</c> while the plugin is not loaded.</param>
  /// <param name="prefs">The user preferences (version and subtitle languages).</param>
  /// <param name="matcher">The library matcher (the files a request covers).</param>
  /// <param name="library">The library manager (resolves the files).</param>
  /// <param name="subtitles">Jellyfin's subtitle manager (searches and downloads subtitles).</param>
  /// <param name="codes">Language codes and the dub language.</param>
  /// <param name="tmdb">The TMDB client (the title's original language).</param>
  /// <param name="notifications">Sends the notice.</param>
  /// <param name="logger">The logger.</param>
  /// <param name="streams">Reads a file's tracks (defaults to Jellyfin's); injectable for tests.</param>
  /// <param name="delay">How long to wait before following up (defaults to three minutes); injectable for tests.</param>
  public AvailabilityFollowUp(
    Func<PluginConfiguration?> config,
    IUserPrefsStore prefs,
    ILibraryMatcher matcher,
    ILibraryManager library,
    ISubtitleManager subtitles,
    LanguageCodes codes,
    ITmdbClient tmdb,
    INotificationService notifications,
    ILogger<AvailabilityFollowUp> logger,
    Func<BaseItem, IReadOnlyList<MediaStream>>? streams = null,
    TimeSpan? delay = null)
  {
    _config = config;
    _prefs = prefs;
    _matcher = matcher;
    _library = library;
    _subtitles = subtitles;
    _codes = codes;
    _tmdb = tmdb;
    _notifications = notifications;
    _logger = logger;
    _streams = streams ?? (item => item.GetMediaStreams());
    _delay = delay ?? DefaultDelay;
  }

  /// <inheritdoc />
  public void Schedule(IReadOnlyList<RequestRecord> available)
  {
    var config = _config();
    if (available is null || available.Count == 0 || config is null
        || !(config.LanguagePreferencesEnabled || config.SubtitleDownloadsEnabled))
    {
      return;
    }

    var snapshot = available.ToList();
    _ = Task.Run(async () =>
    {
      try
      {
        await Task.Delay(_delay).ConfigureAwait(false);
        await ProcessAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
      }
#pragma warning disable CA1031 // Background follow-up: log and carry on.
      catch (Exception ex)
#pragma warning restore CA1031
      {
        _logger.LogWarning(ex, "Jelly Crowd: the language follow-up of newly available requests failed.");
      }
    });
  }

  /// <summary>
  /// Follows up a batch of newly available requests, one requester and title (or season) at a time.
  /// </summary>
  /// <param name="available">The requests.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes once done.</returns>
  internal async Task ProcessAsync(IReadOnlyList<RequestRecord> available, CancellationToken cancellationToken)
  {
    foreach (var group in available.GroupBy(r => (r.UserId, r.TmdbId, r.MediaType, r.Season)))
    {
      try
      {
        await ProcessTitleAsync(group.ToList(), cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        _logger.LogWarning(ex, "Jelly Crowd: could not follow up {Title} for user {UserId}.", group.First().Title, group.Key.UserId);
      }
    }
  }

  private async Task ProcessTitleAsync(List<RequestRecord> requests, CancellationToken cancellationToken)
  {
    var config = _config();
    if (config is null)
    {
      return;
    }

    var first = requests[0];
    var prefs = await _prefs.GetAsync(first.UserId, cancellationToken).ConfigureAwait(false);
    var preference = config.LanguagePreferencesEnabled ? LanguagePolicy.Normalize(prefs.LanguagePreference) : string.Empty;
    var dub = _codes.DubLanguage(config);
    var wanted = config.SubtitleDownloadsEnabled
      ? LanguagePolicy.SubtitlesWanted(preference, prefs.SubtitleLanguages, dub)
      : Array.Empty<string>();
    if (preference.Length == 0 && wanted.Count == 0)
    {
      return;
    }

    var originalLanguage = preference.Length == 0 ? null : await OriginalLanguageAsync(first, cancellationToken).ConfigureAwait(false);
    var notice = LanguageNotice.None;
    foreach (var video in Videos(requests))
    {
      var streams = _streams(video);
      var subtitleLanguages = streams.Where(s => s.Type == MediaStreamType.Subtitle).Select(s => _codes.TwoLetter(s.Language)).ToList();
      foreach (var language in wanted.Where(l => !subtitleLanguages.Contains(l, StringComparer.OrdinalIgnoreCase)))
      {
        if (await FetchSubtitleAsync(video, language, cancellationToken).ConfigureAwait(false))
        {
          subtitleLanguages.Add(language);
        }
      }

      if (notice == LanguageNotice.None && preference.Length > 0)
      {
        var audio = streams.Where(s => s.Type == MediaStreamType.Audio).Select(s => _codes.TwoLetter(s.Language)).ToList();
        notice = LanguagePolicy.Evaluate(preference, originalLanguage, audio, subtitleLanguages, dub);
      }
    }

    if (notice != LanguageNotice.None)
    {
      Announce(config, first, notice, dub);
    }
  }

  // The files a requester's title covers: the movie, the episode, a season's or the series' episodes.
  private List<Video> Videos(List<RequestRecord> requests)
  {
    var ids = new List<string?>();
    foreach (var request in requests)
    {
      if (!string.Equals(request.MediaType, "tv", StringComparison.Ordinal))
      {
        ids.Add(_matcher.FindItemId(request.MediaType, request.TmdbId));
      }
      else if (request.Episode is int episode)
      {
        ids.Add(_matcher.FindEpisodeItemId(request.TmdbId, request.Season, episode));
      }
      else
      {
        ids.AddRange((_matcher.ListEpisodeKeys(request.TmdbId, request.Season) ?? Array.Empty<EpisodeKey>())
          .OrderBy(k => k.Season).ThenBy(k => k.Episode)
          .Select(k => _matcher.FindEpisodeItemId(request.TmdbId, k.Season, k.Episode)));
      }
    }

    return ids
      .Where(id => Guid.TryParse(id, out _))
      .Select(id => Guid.Parse(id!))
      .Distinct()
      .Take(MaxVideosPerTitle)
      .Select(id => _library.GetItemById(id) as Video)
      .OfType<Video>()
      .ToList();
  }

  // Searches and downloads the best acceptable subtitle in one language; whether one was added.
  private async Task<bool> FetchSubtitleAsync(Video video, string language, CancellationToken cancellationToken)
  {
    var threeLetter = _codes.ThreeLetter(language);
    if (string.IsNullOrEmpty(threeLetter))
    {
      return false;
    }

    try
    {
      var results = await _subtitles.SearchSubtitles(video, threeLetter, null, true, cancellationToken).ConfigureAwait(false);
      var best = SubtitlePicker.Pick(results);
      if (best is null)
      {
        return false;
      }

      await _subtitles.DownloadSubtitles(video, best.Id, cancellationToken).ConfigureAwait(false);
      return true;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogInformation(ex, "Jelly Crowd: no {Language} subtitle could be added to {Item}.", language, video.Name);
      return false;
    }
  }

  private async Task<string?> OriginalLanguageAsync(RequestRecord request, CancellationToken cancellationToken)
  {
    try
    {
      var details = await _tmdb.GetDetailsAsync(request.MediaType, request.TmdbId, "en-US", cancellationToken).ConfigureAwait(false);
      return _codes.TwoLetter(details?.OriginalLanguage);
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
    {
      return null;
    }
  }

  private void Announce(PluginConfiguration config, RequestRecord request, LanguageNotice notice, string dub)
  {
    var strings = ServerStrings.ForMember(config.Language, request.UserId);
    var language = LanguageName(strings, dub);
    // A dedicated label when the catalog has one ("VF" for a French dub in French), else "<language> dub".
    var dubKey = "lang_dub_" + dub;
    var dubLabel = strings(dubKey);
    if (dubLabel == dubKey)
    {
      dubLabel = strings("lang_dub_label").Replace("{language}", language, StringComparison.Ordinal);
    }

    var title = request.Season is int season && string.Equals(request.MediaType, "tv", StringComparison.Ordinal)
      ? request.Title + " (S" + season.ToString(CultureInfo.InvariantCulture) + ")"
      : request.Title;
    var key = notice switch
    {
      LanguageNotice.DubbedOnly => "lang_notice_dubbed_only",
      LanguageNotice.OriginalOnly => "lang_notice_original_only",
      _ => "lang_notice_no_subtitles"
    };
    var body = strings(key)
      .Replace("{title}", title, StringComparison.Ordinal)
      .Replace("{dub}", dubLabel, StringComparison.Ordinal)
      .Replace("{language}", language, StringComparison.Ordinal);
    var kind = RequestScheduling.WasUnreleasedRequest(request) ? PersonalNotifyKind.AvailableUnreleased : PersonalNotifyKind.AvailableReleased;
    _ = _notifications.NotifyPersonalAsync(request.UserId, kind, request.Title, strings("lang_notice_subject"), body, request.PosterPath, CancellationToken.None);
  }

  // The language's name in the notice language, from the catalogs; Jellyfin's English name otherwise.
  private string LanguageName(Func<string, string> strings, string language)
  {
    var key = "lang_" + language;
    var name = strings(key);
    return string.IsNullOrEmpty(name) || name == key
      ? _codes.DisplayName(language)
      : name;
  }
}
