using System;
using System.Collections.Generic;
using System.Globalization;
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
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<JellyCrowdSegmentProvider> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="JellyCrowdSegmentProvider"/> class.
  /// </summary>
  /// <param name="libraryManager">The library manager (resolves the item + its file path).</param>
  /// <param name="mediaEncoder">The media encoder (supplies the ffmpeg path).</param>
  /// <param name="processRunner">The process runner (runs the ffmpeg analysis).</param>
  /// <param name="introStore">The intro cache populated by the analysis task.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public JellyCrowdSegmentProvider(
    ILibraryManager libraryManager,
    IMediaEncoder mediaEncoder,
    IProcessRunner processRunner,
    IIntroStore introStore,
    Func<PluginConfiguration> config,
    ILogger<JellyCrowdSegmentProvider> logger)
  {
    _libraryManager = libraryManager;
    _mediaEncoder = mediaEncoder;
    _processRunner = processRunner;
    _introStore = introStore;
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
  /// Removes any extracted analysis data for an item. No-op — this provider caches nothing on disk.
  /// </summary>
  /// <param name="itemId">The item whose extracted data would be cleaned.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A completed task.</returns>
  public Task CleanupExtractedData(Guid itemId, CancellationToken cancellationToken) => Task.CompletedTask;

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

    var runtimeTicks = item.RunTimeTicks.Value;
    var runtimeSeconds = runtimeTicks / (double)TicksPerSecond;
    var outroSegments = await DetectOutroAsync(item.Path, runtimeSeconds, config, cancellationToken).ConfigureAwait(false);
    foreach (var seg in outroSegments)
    {
      segments.Add(new MediaSegmentDto
      {
        ItemId = item.Id,
        Type = MediaSegmentType.Outro,
        StartTicks = (long)(seg.Start * TicksPerSecond),
        EndTicks = (long)(seg.End * TicksPerSecond)
      });
    }

    if (outroSegments.Count > 0)
    {
      _logger.LogInformation(
        "Jelly Crowd: {Count} outro segment(s) for {Name}, first at {Start:0}s.",
        outroSegments.Count,
        item.Name,
        outroSegments[0].Start);
    }

    return segments;
  }

  private async Task<IReadOnlyList<DetectedRegion>> DetectOutroAsync(string path, double runtimeSeconds, PluginConfiguration config, CancellationToken cancellationToken)
  {
    var ffmpeg = _mediaEncoder.EncoderPath;
    if (string.IsNullOrEmpty(ffmpeg))
    {
      return Array.Empty<DetectedRegion>();
    }

    // Analyze only the tail — the last 20% of the runtime, capped so a long movie scan stays fast.
    var window = Math.Clamp(runtimeSeconds * 0.20, 120, config.OutroAnalyzeMaxSeconds);
    var offset = Math.Max(0, runtimeSeconds - window);
    var analyzed = runtimeSeconds - offset;

    // One decode of the tail yields both signals: per-second average luma (signalstats) to tell dark
    // credits from a bright bonus, and audio silence (a quiet credits crawl).
    var args = string.Format(
      CultureInfo.InvariantCulture,
      "-hide_banner -nostats -ss {0:0.###} -i \"{1}\" -vf fps=1,signalstats,metadata=print -af silencedetect=noise=-45dB:d=0.8 -f null -",
      offset,
      path);

    var options = new OutroDetectionOptions(
      config.OutroDarkFraction,
      config.OutroMinCreditRunSeconds,
      config.OutroMinBonusRunSeconds,
      config.OutroMaxBonusGapSeconds,
      config.OutroMaxTrailingBonusSeconds,
      config.OutroMinTrailingSilenceSeconds,
      config.OutroSilenceEndToleranceSeconds,
      config.OutroMinCreditsSeconds,
      config.OutroMaxCreditsSeconds);

    try
    {
      var output = await _processRunner.RunCaptureAsync(ffmpeg, args, config.OutroAnalyzeTimeoutSeconds, cancellationToken).ConfigureAwait(false);
      var (_, silence) = SegmentDetection.ParseRegions(output, analyzed);
      var luma = SegmentDetection.ParseLumaSamples(output);
      return SegmentDetection.DetectOutroSegments(luma, silence, offset, runtimeSeconds, options);
    }
#pragma warning disable CA1031 // Detection is best-effort; a failure just yields no segment.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Jelly Crowd outro detection failed for {Path}.", path);
      return Array.Empty<DetectedRegion>();
    }
  }
}
