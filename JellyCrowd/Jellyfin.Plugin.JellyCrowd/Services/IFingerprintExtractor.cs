using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Extracts a Chromaprint audio fingerprint from a window of a media file: the start of the file for
/// intro detection, or its tail for outro detection (an anime ED is a recurring sequence, just like an OP).
/// </summary>
public interface IFingerprintExtractor
{
  /// <summary>
  /// Fingerprints <paramref name="seconds"/> of the file's primary audio track, starting at
  /// <paramref name="offsetSeconds"/>.
  /// </summary>
  /// <param name="path">The media file path.</param>
  /// <param name="offsetSeconds">Where to start fingerprinting (0 = the start of the file).</param>
  /// <param name="seconds">How many seconds to fingerprint.</param>
  /// <param name="timeoutSeconds">Process timeout.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The sub-fingerprint array, or empty when extraction fails.</returns>
  Task<uint[]> ExtractAsync(string path, double offsetSeconds, int seconds, int timeoutSeconds, CancellationToken cancellationToken);
}
