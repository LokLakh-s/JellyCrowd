using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Extracts a Chromaprint audio fingerprint from the start of a media file, for intro detection.
/// </summary>
public interface IFingerprintExtractor
{
  /// <summary>
  /// Fingerprints the first <paramref name="seconds"/> of the file's primary audio track.
  /// </summary>
  /// <param name="path">The media file path.</param>
  /// <param name="seconds">How many seconds from the start to fingerprint.</param>
  /// <param name="timeoutSeconds">Process timeout.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The sub-fingerprint array, or empty when extraction fails.</returns>
  Task<uint[]> ExtractAsync(string path, int seconds, int timeoutSeconds, CancellationToken cancellationToken);
}
