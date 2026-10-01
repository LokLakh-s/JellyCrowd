using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// One lock per title (movie or series): the operations Jelly Crowd runs on the download backend for the
/// same title — dispatch, retry, cancel, purge, stalled-download recovery — happen one at a time. Users act
/// faster than the backend: a request cancelled while its dispatch is still adding the series to Sonarr
/// would otherwise be cleaned up first and monitored again by the dispatch finishing after it.
/// </summary>
public sealed class TitleOperationLock
{
  private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

  /// <summary>
  /// Waits for the title's lock. Dispose the result to release it.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB id of the title.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A handle that releases the lock when disposed.</returns>
  public async Task<IDisposable> EnterAsync(string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    var key = mediaType + ":" + tmdbId.ToString(CultureInfo.InvariantCulture);
    var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    return new Releaser(gate);
  }

  private sealed class Releaser : IDisposable
  {
    private SemaphoreSlim? _gate;

    public Releaser(SemaphoreSlim gate) => _gate = gate;

    public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
  }
}
