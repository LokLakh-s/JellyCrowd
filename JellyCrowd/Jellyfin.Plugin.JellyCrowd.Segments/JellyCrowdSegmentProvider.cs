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
  private const double MinBlackSeconds = 0.4;

  private readonly ILibraryManager _libraryManager;
  private readonly IMediaEncoder _mediaEncoder;
  private readonly IProcessRunner _processRunner;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<JellyCrowdSegmentProvider> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="JellyCrowdSegmentProvider"/> class.
  /// </summary>
  /// <param name="libraryManager">The library manager (resolves the item + its file path).</param>
  /// <param name="mediaEncoder">The media encoder (supplies the ffmpeg path).</param>
  /// <param name="processRunner">The process runner (runs the ffmpeg analysis).</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public JellyCrowdSegmentProvider(
    ILibraryManager libraryManager,
    IMediaEncoder mediaEncoder,
    IProcessRunner processRunner,
    Func<PluginConfiguration> config,
    ILogger<JellyCrowdSegmentProvider> logger)
  {
    _libraryManager = libraryManager;
    _mediaEncoder = mediaEncoder;
    _processRunner = processRunner;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd";

  /// <inheritdoc />
  public ValueTask<bool> Supports(BaseItem item)
  {
    ArgumentNullException.ThrowIfNull(item);
    return ValueTask.FromResult(_config().SkipOutroEnabled && item is Episode or Movie);
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
    if (!config.SkipOutroEnabled)
    {
      return segments;
    }

    var item = _libraryManager.GetItemById(request.ItemId);
    if (item is not (Episode or Movie) || string.IsNullOrEmpty(item.Path) || item.RunTimeTicks is not > 0)
    {
      return segments;
    }

    var runtimeTicks = item.RunTimeTicks.Value;
    var runtimeSeconds = runtimeTicks / (double)TicksPerSecond;
    var outroStart = await DetectOutroAsync(item.Path, runtimeSeconds, config, cancellationToken).ConfigureAwait(false);
    if (outroStart is not null)
    {
      segments.Add(new MediaSegmentDto
      {
        ItemId = item.Id,
        Type = MediaSegmentType.Outro,
        StartTicks = (long)(outroStart.Value * TicksPerSecond),
        EndTicks = runtimeTicks
      });
      _logger.LogInformation("Jelly Crowd: outro at {Start:0}s for {Name}.", outroStart.Value, item.Name);
    }

    return segments;
  }

  private async Task<double?> DetectOutroAsync(string path, double runtimeSeconds, PluginConfiguration config, CancellationToken cancellationToken)
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

    var args = string.Format(
      CultureInfo.InvariantCulture,
      "-hide_banner -nostats -ss {0:0.###} -i \"{1}\" -vf blackdetect=d={2:0.###}:pix_th=0.10 -af silencedetect=noise=-45dB:d=0.8 -f null -",
      offset,
      path,
      MinBlackSeconds);

    try
    {
      var output = await _processRunner.RunCaptureAsync(ffmpeg, args, config.OutroAnalyzeTimeoutSeconds, cancellationToken).ConfigureAwait(false);
      var (black, silence) = SegmentDetection.ParseRegions(output, analyzed);
      return SegmentDetection.DetectOutroStartSeconds(
        black,
        silence,
        offset,
        runtimeSeconds,
        config.OutroMinLongBlackSeconds,
        config.OutroMinSilenceRunSeconds,
        config.OutroSilenceEndToleranceSeconds,
        config.OutroMinCreditsSeconds,
        config.OutroMaxCreditsSeconds);
    }
#pragma warning disable CA1031 // Detection is best-effort; a failure just yields no segment.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Jelly Crowd outro detection failed for {Path}.", path);
      return null;
    }
  }
}
