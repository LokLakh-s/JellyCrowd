using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Scheduled task that fingerprints the TAIL of each season's episodes and caches their shared end-credits
/// sequence, so the media-segment provider can offer a "Skip Outro" button on it.
/// <para>
/// The brightness/silence heuristic (see the segment provider) only recognises credits that are dark and
/// quiet. An anime's ED — and any credits played over a song — is bright and sung, so that heuristic is
/// blind to it: measured across this library it found credits on virtually every western show and on
/// almost no anime. But an ED is the SAME sequence in every episode of a season, exactly like an OP, so
/// the very fingerprinting that already detects intros finds it reliably.
/// </para>
/// </summary>
public sealed class OutroAnalysisTask : IScheduledTask
{
  private const long TicksPerSecond = 10_000_000;

  // Recorded for an episode that was analyzed but shares no credits with its siblings — distinct from
  // "not yet analyzed" (absent), and read by the provider as "fall back to the heuristic".
  private static readonly OutroRegion NoOutro = new(-1, -1);

  private readonly ILibraryManager _libraryManager;
  private readonly IFingerprintExtractor _extractor;
  private readonly IOutroSegmentStore _store;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<OutroAnalysisTask> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="OutroAnalysisTask"/> class.
  /// </summary>
  /// <param name="libraryManager">The Jellyfin library manager.</param>
  /// <param name="extractor">The audio fingerprint extractor.</param>
  /// <param name="store">The end-credits cache.</param>
  /// <param name="config">The plugin configuration accessor.</param>
  /// <param name="logger">The logger.</param>
  public OutroAnalysisTask(ILibraryManager libraryManager, IFingerprintExtractor extractor, IOutroSegmentStore store, Func<PluginConfiguration> config, ILogger<OutroAnalysisTask> logger)
  {
    _libraryManager = libraryManager;
    _extractor = extractor;
    _store = store;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd: analyze outros";

  /// <inheritdoc />
  public string Key => "JellyCrowdAnalyzeOutros";

  /// <inheritdoc />
  public string Description => "Fingerprints the end of each season's episodes to detect their shared end-credits sequence (an anime ED, a recurring credits song) for the native Skip Outro button.";

  /// <inheritdoc />
  public string Category => "Jelly Crowd";

  /// <inheritdoc />
  public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(progress);

    var config = _config();
    if (!config.SkipOutroEnabled)
    {
      progress.Report(100);
      return;
    }

    var window = Math.Max(60, config.OutroFingerprintSeconds);
    var timeout = Math.Max(30, config.IntroAnalyzeTimeoutSeconds);
    var minConfirmations = Math.Max(1, config.IntroMinConfirmations);
    var minDurationSeconds = Math.Max(1, config.OutroMinCreditsSeconds);

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
        .Where(e => !string.IsNullOrEmpty(e.Path) && e.RunTimeTicks is > 0)
        .OrderBy(e => e.IndexNumber ?? int.MaxValue)
        .ToList();

      // Need at least two episodes to find a shared sequence. Re-analyze unless every episode already has
      // a FOUND end-credits region: an episode cached as "analyzed, none" (a transient extraction hiccup,
      // a special/absent ED, or too few siblings to confirm at the time) is retried on the next run, so
      // re-running the task actually fills gaps instead of skipping them forever. Found episodes in a
      // covered season are still skipped, so a routine re-run over a fully-detected library is cheap.
      if (episodes.Count < 2 || episodes.All(e => IsFoundOutro(_store.Get(e.Id))))
      {
        continue;
      }

      // Fingerprint each episode's tail. The window starts at (runtime - window), so frame 0 of a
      // fingerprint is that absolute instant — different per episode, since runtimes differ.
      var offsets = new List<double>(episodes.Count);
      var fingerprints = new List<uint[]>(episodes.Count);
      foreach (var episode in episodes)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var runtimeSeconds = episode.RunTimeTicks!.Value / (double)TicksPerSecond;
        var offset = Math.Max(0, runtimeSeconds - window);
        offsets.Add(offset);
        fingerprints.Add(await _extractor.ExtractAsync(episode.Path, offset, window, timeout, cancellationToken).ConfigureAwait(false));
      }

      var sample = fingerprints.FirstOrDefault(f => f.Length > 0);
      if (sample is null || sample.Length == 0)
      {
        continue;
      }

      var minRunFrames = (int)(minDurationSeconds * (sample.Length / (double)window));
      var shared = FingerprintMatcher.FindSeasonIntros(
        fingerprints.Select(f => (IReadOnlyList<uint>)f).ToList(),
        minRunFrames: minRunFrames,
        minConfirmations: minConfirmations);

      var result = new Dictionary<Guid, OutroRegion>(episodes.Count);
      var found = 0;
      for (var i = 0; i < episodes.Count; i++)
      {
        if (shared[i] is (int start, int end) && fingerprints[i].Length > 0)
        {
          result[episodes[i].Id] = ToAbsoluteRegion(offsets[i], window, fingerprints[i].Length, start, end);
          found++;
        }
        else
        {
          result[episodes[i].Id] = NoOutro;
        }
      }

      _store.UpsertSeason(result);
      if (found > 0)
      {
        _logger.LogInformation(
          "Jelly Crowd: cached {Found}/{Total} end-credits sequence(s) for {Season}.",
          found.ToString(CultureInfo.InvariantCulture),
          episodes.Count.ToString(CultureInfo.InvariantCulture),
          seasons[s].Name);
      }
    }

    progress.Report(100);
  }

  /// <summary>
  /// Turns a frame range within an episode's TAIL fingerprint into an absolute region of the file. The
  /// window starts at <paramref name="offsetSeconds"/>, so frame 0 is that instant, not the start of the
  /// file — getting this wrong would place every Skip Outro button minutes away from the credits.
  /// </summary>
  /// <param name="offsetSeconds">Where the fingerprinted window begins in the file.</param>
  /// <param name="windowSeconds">How many seconds the window spans.</param>
  /// <param name="frameCount">How many fingerprint frames that window produced.</param>
  /// <param name="startFrame">First frame of the shared run.</param>
  /// <param name="endFrame">Last frame of the shared run (inclusive).</param>
  /// <returns>The region, in absolute ticks.</returns>
  internal static OutroRegion ToAbsoluteRegion(double offsetSeconds, int windowSeconds, int frameCount, int startFrame, int endFrame)
  {
    var secondsPerFrame = windowSeconds / (double)frameCount;
    var startTicks = (long)((offsetSeconds + (startFrame * secondsPerFrame)) * TicksPerSecond);
    var endTicks = (long)((offsetSeconds + ((endFrame + 1) * secondsPerFrame)) * TicksPerSecond);
    return new OutroRegion(startTicks, endTicks);
  }

  /// <summary>
  /// Whether a cached result is a real end-credits region (found), as opposed to the "analyzed, none"
  /// sentinel or an absent entry — the two cases a re-run should retry.
  /// </summary>
  /// <param name="cached">The store's cached region for an episode, or <c>null</c>.</param>
  /// <returns><c>true</c> when it is a usable region.</returns>
  internal static bool IsFoundOutro(OutroRegion? cached)
    => cached is { StartTicks: >= 0 } region && region.EndTicks > region.StartTicks;

  /// <inheritdoc />
  public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
}
