using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Segments;

/// <summary>
/// Supplies native Jellyfin media segments so the player shows a built-in "Skip Outro" button on movies
/// and episodes. Detection is heuristic and ffmpeg-based: the tail of the file is analyzed for the
/// end-of-content fade to black, which marks where the end credits start. Isolated in its own assembly
/// so a breaking change to IMediaSegmentProvider on a newer Jellyfin cannot break the whole plugin.
/// </summary>
public sealed class JellyCrowdSegmentProvider : IMediaSegmentProvider
{
  private const long TicksPerSecond = TimeSpan.TicksPerSecond;

  private readonly ILibraryManager _libraryManager;
  private readonly IMediaEncoder _mediaEncoder;
  private readonly IProcessRunner _processRunner;
  private readonly IIntroStore _introStore;
  private readonly IOutroStore _outroStore;
  private readonly IOutroSegmentStore _outroSegmentStore;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<JellyCrowdSegmentProvider> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="JellyCrowdSegmentProvider"/> class.
  /// </summary>
  /// <param name="libraryManager">The library manager (resolves the item + its file path).</param>
  /// <param name="mediaEncoder">The media encoder (supplies the ffmpeg path).</param>
  /// <param name="processRunner">The process runner (runs the ffmpeg analysis).</param>
  /// <param name="introStore">The intro cache populated by the analysis task.</param>
  /// <param name="outroStore">The outro cache: remembers each item's analysis so a scan doesn't re-run ffmpeg.</param>
  /// <param name="outroSegmentStore">The end-credits found by fingerprinting a season's episode tails.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public JellyCrowdSegmentProvider(
    ILibraryManager libraryManager,
    IMediaEncoder mediaEncoder,
    IProcessRunner processRunner,
    IIntroStore introStore,
    IOutroStore outroStore,
    IOutroSegmentStore outroSegmentStore,
    Func<PluginConfiguration> config,
    ILogger<JellyCrowdSegmentProvider> logger)
  {
    _libraryManager = libraryManager;
    _mediaEncoder = mediaEncoder;
    _processRunner = processRunner;
    _introStore = introStore;
    _outroStore = outroStore;
    _outroSegmentStore = outroSegmentStore;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd";

  /// <inheritdoc />
  public ValueTask<bool> Supports(BaseItem item)
  {
    ArgumentNullException.ThrowIfNull(item);
    var config = _config();
    var supported = (config.SkipOutroEnabled && item is Episode or Movie)
      || (config.SkipIntroEnabled && item is Episode);
    return ValueTask.FromResult(supported);
  }

  /// <summary>
  /// Forgets the remembered outro analysis for an item, so it is analyzed afresh next scan. Jellyfin calls
  /// this when it cleans an item's extracted data (item removal / a forced regeneration), not on a routine
  /// scan — so the cache survives ordinary runs.
  /// </summary>
  /// <param name="itemId">The item whose extracted data is being cleaned.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A completed task.</returns>
  public Task CleanupExtractedData(Guid itemId, CancellationToken cancellationToken)
  {
    _outroStore.Remove(itemId);
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(MediaSegmentGenerationRequest request, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);
    var segments = new List<MediaSegmentDto>();

    var config = _config();
    var item = _libraryManager.GetItemById(request.ItemId);
    if (item is not (Episode or Movie) || string.IsNullOrEmpty(item.Path) || item.RunTimeTicks is not > 0)
    {
      return segments;
    }

    // Intro (episodes only): served from the cache the analysis task populated by fingerprinting the season.
    if (config.SkipIntroEnabled && item is Episode)
    {
      var intro = _introStore.Get(item.Id);
      if (intro is not null && intro.StartTicks >= 0 && intro.EndTicks > intro.StartTicks)
      {
        segments.Add(new MediaSegmentDto
        {
          ItemId = item.Id,
          Type = MediaSegmentType.Intro,
          StartTicks = intro.StartTicks,
          EndTicks = intro.EndTicks
        });
      }
    }

    if (!config.SkipOutroEnabled)
    {
      return segments;
    }

    // A season's shared end-credits, found by fingerprint, wins over the brightness/silence heuristic:
    // it is exact, it costs nothing here (the task already did the work), and it is the ONLY thing that
    // sees credits that are bright and sung — an anime ED, which the heuristic is blind to by construction.
    var fingerprinted = _outroSegmentStore.Get(item.Id);
    if (fingerprinted is { StartTicks: >= 0 } credits && credits.EndTicks > credits.StartTicks)
    {
      segments.Add(new MediaSegmentDto
      {
        ItemId = item.Id,
        Type = MediaSegmentType.Outro,
        StartTicks = credits.StartTicks,
        EndTicks = credits.EndTicks
      });
      return segments;
    }

    var runtimeTicks = item.RunTimeTicks.Value;
    var options = BuildOutroOptions(config);
    var signature = SegmentDetection.OutroOptionsSignature(options, config.OutroAnalyzeMaxSeconds);
    var sizeBytes = item.Size ?? -1;
    var modifiedTicks = item.DateModified.Ticks;

    // Serve a remembered analysis when the file and the tuning are unchanged — the whole point is to not
    // re-run the ffmpeg tail decode on every Media Segment Scan. An empty cached region list is a genuine
    // result ("analyzed, no outro"), so it is reused too rather than re-analyzed.
    var cached = _outroStore.Get(item.Id);
    IReadOnlyList<OutroRegion> regions;
    if (cached is not null && cached.Matches(sizeBytes, modifiedTicks, runtimeTicks, signature))
    {
      regions = cached.Regions ?? Array.Empty<OutroRegion>();
    }
    else
    {
      var runtimeSeconds = runtimeTicks / (double)TicksPerSecond;
      var detected = await DetectOutroAsync(item.Path, runtimeSeconds, config, options, cancellationToken).ConfigureAwait(false);
      if (detected is null)
      {
        // Analysis could not run (no ffmpeg, timeout, decode error): don't remember a failure as "no
        // outro" — leave the item uncached so the next scan retries it.
        return segments;
      }

      regions = detected
        .Select(r => new OutroRegion((long)(r.Start * TicksPerSecond), (long)(r.End * TicksPerSecond)))
        .ToList();
      _outroStore.Set(item.Id, new OutroAnalysis(sizeBytes, modifiedTicks, runtimeTicks, signature, regions));
    }

    foreach (var region in regions)
    {
      segments.Add(new MediaSegmentDto
      {
        ItemId = item.Id,
        Type = MediaSegmentType.Outro,
        StartTicks = region.StartTicks,
        EndTicks = region.EndTicks
      });
    }

    if (regions.Count > 0)
    {
      _logger.LogInformation(
        "Jelly Crowd: {Count} outro segment(s) for {Name}, first at {Start:0}s.",
        regions.Count,
        item.Name,
        regions[0].StartTicks / (double)TicksPerSecond);
    }

    return segments;
  }

  private static OutroDetectionOptions BuildOutroOptions(PluginConfiguration config)
    => new(
      config.OutroDarkFraction,
      config.OutroMinCreditRunSeconds,
      config.OutroMinBonusRunSeconds,
      config.OutroMaxBonusGapSeconds,
      config.OutroMaxTrailingBonusSeconds,
      config.OutroMinTrailingSilenceSeconds,
      config.OutroSilenceEndToleranceSeconds,
      config.OutroMinCreditsSeconds,
      config.OutroMaxCreditsSeconds);

  // Returns the detected outro regions (possibly empty), or null when the analysis itself could not run
  // (no ffmpeg / timeout / decode error) — so the caller can retry next scan instead of caching a failure.
  // Turn one ffmpeg run's output (per-second luma + silences) into the outro regions.
  private static IReadOnlyList<DetectedRegion> Interpret(string output, double analyzed, double offset, double runtimeSeconds, OutroDetectionOptions options)
  {
    var (_, silence) = SegmentDetection.ParseRegions(output, analyzed);
    var luma = SegmentDetection.ParseLumaSamples(output);
    return SegmentDetection.DetectOutroSegments(luma, silence, offset, runtimeSeconds, options);
  }

  private async Task<IReadOnlyList<DetectedRegion>?> DetectOutroAsync(string path, double runtimeSeconds, PluginConfiguration config, OutroDetectionOptions options, CancellationToken cancellationToken)
  {
    var ffmpeg = _mediaEncoder.EncoderPath;
    if (string.IsNullOrEmpty(ffmpeg))
    {
      return null;
    }

    // Analyze only the tail — the last 20% of the runtime, capped so a long movie scan stays fast.
    var window = Math.Clamp(runtimeSeconds * 0.20, 120, config.OutroAnalyzeMaxSeconds);
    var offset = Math.Max(0, runtimeSeconds - window);
    var analyzed = runtimeSeconds - offset;

    // One decode of the tail yields both signals: per-second average luma (signalstats) to tell dark
    // credits from a bright bonus, and audio silence (a quiet credits crawl). The video decode — the heavy
    // part of the Media Segment Scan — is offloaded to the GPU per the configured hardware-accel mode.
    // A fixed timeout cannot serve both a 20-minute SD episode and a 4K HEVC one, so give the decode at
    // least a second of budget per second of video analyzed: too little and heavy files fail forever,
    // never caching a result and re-burning the whole budget on every scan.
    var timeout = Math.Max(config.OutroAnalyzeTimeoutSeconds, (int)Math.Ceiling(analyzed));
    var args = SegmentDetection.BuildOutroAnalyzeArgs(config.SegmentHwAccel, offset, path);

    try
    {
      var output = await _processRunner.RunCaptureAsync(ffmpeg, args, timeout, cancellationToken).ConfigureAwait(false);
      return Interpret(output, analyzed, offset, runtimeSeconds, options);
    }
#pragma warning disable CA1031 // Detection is best-effort; a failure just yields no segment this run.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      // The accelerated pipeline can fail on a driver that lacks the hardware scaler, or on an exotic
      // pixel format. Retry once on the CPU before giving up, so such a setup still gets its outros.
      if (!string.IsNullOrEmpty(SegmentDetection.HwAccelArg(config.SegmentHwAccel)))
      {
        try
        {
          _logger.LogWarning(ex, "Jelly Crowd: accelerated outro analysis failed for {Path}; retrying on the CPU.", path);
          var output = await _processRunner.RunCaptureAsync(ffmpeg, SegmentDetection.BuildOutroAnalyzeFallbackArgs(offset, path), timeout, cancellationToken).ConfigureAwait(false);
          return Interpret(output, analyzed, offset, runtimeSeconds, options);
        }
#pragma warning disable CA1031 // Same best-effort contract as above.
        catch (Exception fallbackEx)
#pragma warning restore CA1031
        {
          // Warning, not debug: until now this failed in complete silence, so an admin had no way to see
          // that a whole show would never get a Skip Outro button.
          _logger.LogWarning(fallbackEx, "Jelly Crowd: outro analysis failed for {Path} (CPU fallback too). No outro will be offered.", path);
          return null;
        }
      }

      _logger.LogWarning(ex, "Jelly Crowd: outro analysis failed for {Path}. No outro will be offered.", path);
      return null;
    }
  }
}
