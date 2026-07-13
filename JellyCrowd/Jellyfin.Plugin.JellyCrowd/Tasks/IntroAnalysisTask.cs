using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Tasks;

/// <summary>
/// Scheduled task that fingerprints each season's episodes and caches the shared intro, so the native
/// media-segment provider can expose a "Skip Intro" button. Episodes that share no intro with their
/// siblings (a premiere with no standard OP, a recap) are recorded as "no intro" so the season is not
/// re-analyzed every run.
/// </summary>
public sealed class IntroAnalysisTask : IScheduledTask
{
  private const long TicksPerSecond = TimeSpan.TicksPerSecond;

  // Recorded for an episode that was analyzed but shares no intro — distinct from "not yet analyzed".
  private static readonly IntroSegment NoIntro = new(-1, -1);

  private readonly ILibraryManager _libraryManager;
  private readonly IFingerprintExtractor _extractor;
  private readonly IIntroStore _store;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<IntroAnalysisTask> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="IntroAnalysisTask"/> class.
  /// </summary>
  /// <param name="libraryManager">The library manager (enumerates seasons/episodes).</param>
  /// <param name="extractor">The fingerprint extractor.</param>
  /// <param name="store">The intro cache.</param>
  /// <param name="config">Provides the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public IntroAnalysisTask(ILibraryManager libraryManager, IFingerprintExtractor extractor, IIntroStore store, Func<PluginConfiguration> config, ILogger<IntroAnalysisTask> logger)
  {
    _libraryManager = libraryManager;
    _extractor = extractor;
    _store = store;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd: analyze intros";

  /// <inheritdoc />
  public string Key => "JellyCrowdAnalyzeIntros";

  /// <inheritdoc />
  public string Description => "Fingerprints episodes to detect and cache each season's shared intro for the native Skip Intro button.";

  /// <inheritdoc />
  public string Category => "Jelly Crowd";

  /// <inheritdoc />
  public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(progress);

    var config = _config();
    if (!config.SkipIntroEnabled)
    {
      progress.Report(100);
      return;
    }

    var window = Math.Max(60, config.IntroAnalyzeSeconds);
    var timeout = Math.Max(30, config.IntroAnalyzeTimeoutSeconds);
    var minConfirmations = Math.Max(1, config.IntroMinConfirmations);
    var minDurationSeconds = Math.Max(1, config.IntroMinDurationSeconds);

    var seasons = _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Season },
      Recursive = true
    });

    for (var s = 0; s < seasons.Count; s++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      progress.Report((double)s / Math.Max(1, seasons.Count) * 100);

      var episodes = _libraryManager
        .GetItemList(new InternalItemsQuery
        {
          ParentId = seasons[s].Id,
          IncludeItemTypes = new[] { BaseItemKind.Episode }
        })
        .Where(e => !string.IsNullOrEmpty(e.Path))
        .OrderBy(e => e.IndexNumber ?? int.MaxValue)
        .ToList();

      // Need at least two episodes to find a shared intro; skip a season already fully analyzed.
      if (episodes.Count < 2 || episodes.All(e => _store.Get(e.Id) is not null))
      {
        continue;
      }

      var fingerprints = new List<uint[]>(episodes.Count);
      foreach (var episode in episodes)
      {
        cancellationToken.ThrowIfCancellationRequested();
        fingerprints.Add(await _extractor.ExtractAsync(episode.Path, 0, window, timeout, cancellationToken).ConfigureAwait(false));
      }

      var sample = fingerprints.FirstOrDefault(f => f.Length > 0);
      if (sample is null || sample.Length == 0)
      {
        continue;
      }

      var minRunFrames = (int)(minDurationSeconds * (sample.Length / (double)window));
      var intros = FingerprintMatcher.FindSeasonIntros(
        fingerprints.Select(f => (IReadOnlyList<uint>)f).ToList(),
        minRunFrames: minRunFrames,
        minConfirmations: minConfirmations);

      var result = new Dictionary<Guid, IntroSegment?>(episodes.Count);
      var found = 0;
      for (var i = 0; i < episodes.Count; i++)
      {
        if (intros[i] is (int start, int end) region && fingerprints[i].Length > 0)
        {
          var secondsPerFrame = window / (double)fingerprints[i].Length;
          var startTicks = (long)(start * secondsPerFrame * TicksPerSecond);
          var endTicks = (long)((end + 1) * secondsPerFrame * TicksPerSecond);
          result[episodes[i].Id] = new IntroSegment(startTicks, endTicks);
          found++;
        }
        else
        {
          result[episodes[i].Id] = NoIntro;
        }
      }

      _store.UpsertSeason(result);
      if (found > 0)
      {
        _logger.LogInformation("Jelly Crowd: cached {Found}/{Total} intro(s) for {Season}.", found, episodes.Count, seasons[s].Name);
      }
    }

    progress.Report(100);
  }

  /// <inheritdoc />
  public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
  {
    // Daily off-peak; already-analyzed seasons are skipped, so routine runs are cheap.
    return new[]
    {
      new TaskTriggerInfo
      {
        Type = TaskTriggerInfoType.DailyTrigger,
        TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
      }
    };
  }
}
