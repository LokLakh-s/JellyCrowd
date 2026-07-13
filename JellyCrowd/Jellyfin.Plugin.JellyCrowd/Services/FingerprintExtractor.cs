using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IFingerprintExtractor"/> that drives the bundled ffmpeg's Chromaprint muxer, decoding
/// the primary audio track to mono and emitting the raw <c>uint32</c> sub-fingerprint array on stdout.
/// </summary>
public sealed class FingerprintExtractor : IFingerprintExtractor
{
  private readonly IMediaEncoder _mediaEncoder;
  private readonly IProcessRunner _processRunner;
  private readonly ILogger<FingerprintExtractor> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="FingerprintExtractor"/> class.
  /// </summary>
  /// <param name="mediaEncoder">Supplies the ffmpeg path.</param>
  /// <param name="processRunner">Runs ffmpeg and captures stdout.</param>
  /// <param name="logger">The logger.</param>
  public FingerprintExtractor(IMediaEncoder mediaEncoder, IProcessRunner processRunner, ILogger<FingerprintExtractor> logger)
  {
    _mediaEncoder = mediaEncoder;
    _processRunner = processRunner;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<uint[]> ExtractAsync(string path, double offsetSeconds, int seconds, int timeoutSeconds, CancellationToken cancellationToken)
  {
    var ffmpeg = _mediaEncoder.EncoderPath;
    if (string.IsNullOrEmpty(ffmpeg) || string.IsNullOrEmpty(path))
    {
      return Array.Empty<uint>();
    }

    // Decode N seconds of the primary audio track (from the given offset) to mono 22.05 kHz and print
    // the raw Chromaprint sub-fingerprints (one uint32 per ~0.12s) to stdout. -ss before -i is an input
    // seek, so fingerprinting a tail costs no more than fingerprinting a head.
    var seek = offsetSeconds > 0
      ? string.Format(CultureInfo.InvariantCulture, "-ss {0:0.###} ", offsetSeconds)
      : string.Empty;
    var args = string.Format(
      CultureInfo.InvariantCulture,
      "-hide_banner -nostats {0}-t {1} -i \"{2}\" -map 0:a:0 -ac 1 -ar 22050 -f chromaprint -fp_format raw -",
      seek,
      seconds,
      path);

    try
    {
      var bytes = await _processRunner.RunCaptureBytesAsync(ffmpeg, args, timeoutSeconds, cancellationToken).ConfigureAwait(false);
      var count = bytes.Length / 4;
      if (count == 0)
      {
        return Array.Empty<uint>();
      }

      var fingerprint = new uint[count];
      Buffer.BlockCopy(bytes, 0, fingerprint, 0, count * 4);
      return fingerprint;
    }
#pragma warning disable CA1031 // Fingerprinting is best-effort; a failure just yields no intro for the item.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Jelly Crowd fingerprint extraction failed for {Path}.", path);
      return Array.Empty<uint>();
    }
  }
}
