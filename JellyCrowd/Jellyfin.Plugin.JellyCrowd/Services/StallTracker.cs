using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Tracks per-item download progress across polls to decide when a download is "stalled" — i.e. an
/// in-progress grab (queued/downloading, or flagged with a warning by the *arr) whose progress hasn't
/// advanced for at least the configured threshold. Pure aside from its own in-memory state, so the
/// decision logic is unit-testable. State is reset once an item makes progress, finishes, or leaves
/// the queue.
/// </summary>
public sealed class StallTracker
{
  private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

  /// <summary>
  /// Records the latest observed state of an item and returns whether it should now be recovered
  /// (blocklisted + re-searched). Returns <c>true</c> at most once per stall (the entry is cleared).
  /// </summary>
  /// <param name="key">A stable key for the item (e.g. the request id).</param>
  /// <param name="percent">The current download percentage (0–100).</param>
  /// <param name="state">The mapped queue state (<c>warning</c>/<c>downloading</c>/<c>queued</c>/…).</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="threshold">How long without progress counts as stalled.</param>
  /// <returns><c>true</c> when the item has been stalled for at least <paramref name="threshold"/>.</returns>
  public bool ShouldRecover(string key, double percent, string state, DateTime nowUtc, TimeSpan threshold)
  {
    ArgumentNullException.ThrowIfNull(key);

    // Only mid-flight grabs can stall. Anything else (importing, completed, missing, unreleased) is not
    // a candidate — forget any prior tracking so a later genuine stall starts its clock fresh.
    var isCandidate = string.Equals(state, "warning", StringComparison.Ordinal)
      || string.Equals(state, "downloading", StringComparison.Ordinal)
      || string.Equals(state, "queued", StringComparison.Ordinal);
    if (!isCandidate || percent >= 100)
    {
      _entries.TryRemove(key, out _);
      return false;
    }

    if (!_entries.TryGetValue(key, out var entry) || percent > entry.LastPercent)
    {
      // First sighting, or real progress since last poll → (re)start the clock.
      _entries[key] = new Entry(percent, nowUtc);
      return false;
    }

    if (nowUtc - entry.StalledSinceUtc >= threshold)
    {
      _entries.TryRemove(key, out _); // recovered once; don't fire again until it re-appears
      return true;
    }

    return false;
  }

  /// <summary>Stops tracking an item (e.g. once its request is no longer approved).</summary>
  /// <param name="key">The item key.</param>
  public void Forget(string key)
  {
    if (key is not null)
    {
      _entries.TryRemove(key, out _);
    }
  }

  private readonly record struct Entry(double LastPercent, DateTime StalledSinceUtc);
}
