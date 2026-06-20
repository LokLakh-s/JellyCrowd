using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// In-memory sliding-window rate limiter, keyed (e.g. by user id). Thread-safe and time-injectable so
/// it can be unit tested. Bounded per key by pruning timestamps outside the window on each access.
/// </summary>
public sealed class RateLimiter
{
  private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new(StringComparer.Ordinal);

  /// <summary>
  /// Records a hit for <paramref name="key"/> and returns whether it is allowed within the window.
  /// </summary>
  /// <param name="key">The bucket key (e.g. user id).</param>
  /// <param name="maxPerWindow">Max allowed hits per window (0 or less disables limiting).</param>
  /// <param name="window">The sliding window length.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <returns><c>true</c> when allowed; <c>false</c> when the limit is exceeded.</returns>
  public bool TryAcquire(string key, int maxPerWindow, TimeSpan window, DateTime nowUtc)
  {
    if (maxPerWindow <= 0)
    {
      return true;
    }

    var queue = _hits.GetOrAdd(key, _ => new Queue<DateTime>());
    lock (queue)
    {
      var cutoff = nowUtc - window;
      while (queue.Count > 0 && queue.Peek() < cutoff)
      {
        queue.Dequeue();
      }

      if (queue.Count >= maxPerWindow)
      {
        return false;
      }

      queue.Enqueue(nowUtc);
      return true;
    }
  }
}
