using System;
using System.Collections.Generic;
using System.Numerics;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure Chromaprint fingerprint alignment. Finds the audio an episode shares with its season siblings —
/// the intro — by voting for the alignment offset from exact sub-fingerprint matches, then growing the
/// longest run whose per-frame Hamming distance stays low. Process- and IO-free so it is unit-tested
/// against captured fingerprints.
/// </summary>
public static class FingerprintMatcher
{
  /// <summary>Number of top voted offsets to evaluate (an early logo can out-vote the intro).</summary>
  private const int OffsetsToTry = 6;

  /// <summary>
  /// Finds the longest contiguous region the two fingerprints share.
  /// </summary>
  /// <param name="a">Episode A sub-fingerprints.</param>
  /// <param name="b">Episode B sub-fingerprints.</param>
  /// <param name="maxBitDistance">Max Hamming distance (of the 32-bit sub-fingerprints) to count as a frame match.</param>
  /// <param name="maxGap">Frames of mismatch tolerated inside a run before it is cut.</param>
  /// <param name="minRunFrames">Minimum run length to return; shorter shared bits (logos, stings) are ignored.</param>
  /// <returns>The shared region, or <c>null</c> when nothing long enough is shared.</returns>
  public static FingerprintMatch? FindSharedRegion(
    IReadOnlyList<uint> a,
    IReadOnlyList<uint> b,
    int maxBitDistance = 6,
    int maxGap = 8,
    int minRunFrames = 120)
  {
    ArgumentNullException.ThrowIfNull(a);
    ArgumentNullException.ThrowIfNull(b);
    if (a.Count == 0 || b.Count == 0)
    {
      return null;
    }

    FingerprintMatch? best = null;
    foreach (var offset in TopOffsets(a, b))
    {
      var run = LongestRun(a, b, offset, maxBitDistance, maxGap);
      if (run is FingerprintMatch m && m.Length >= minRunFrames && (best is null || m.Length > best.Value.Length))
      {
        best = m;
      }
    }

    return best;
  }

  /// <summary>
  /// Derives each episode's intro from the whole season. Every episode is aligned against the others; the
  /// intro is the region of that episode confirmed by at least <paramref name="minConfirmations"/> siblings
  /// <em>that agree on where it is</em>. Episodes that share no long segment (a premiere with no standard
  /// OP, a recap) get <c>null</c>.
  /// <para>
  /// Two guards keep noise out. Siblings only count when their matches overlap: a quiet, sparsely-scored
  /// show fingerprints alike in unrelated places, and averaging such matches invents a region no sibling
  /// ever confirmed — a skip button in the middle of a scene. And a real OP/ED recurs across the SEASON,
  /// so when fewer than <paramref name="minSeasonCoveragePercent"/> of the episodes share it, the whole
  /// season is dropped rather than served: a couple of episodes agreeing on a 20-second stretch is
  /// coincidence, not a title sequence.
  /// </para>
  /// </summary>
  /// <param name="episodes">Each episode's sub-fingerprints (season order).</param>
  /// <param name="maxBitDistance">Per-frame Hamming tolerance.</param>
  /// <param name="maxGap">Mismatch tolerance inside a run.</param>
  /// <param name="minRunFrames">Minimum intro length in frames.</param>
  /// <param name="minConfirmations">How many agreeing siblings must confirm before an intro is accepted.</param>
  /// <param name="minSeasonCoveragePercent">Percentage of the season that must share the sequence for any
  /// of it to be kept; <c>0</c> disables the season-wide check.</param>
  /// <returns>Per-episode intro region as inclusive frame indices <c>(Start, End)</c>, or <c>null</c>.</returns>
  public static IReadOnlyList<(int Start, int End)?> FindSeasonIntros(
    IReadOnlyList<IReadOnlyList<uint>> episodes,
    int maxBitDistance = 6,
    int maxGap = 8,
    int minRunFrames = 120,
    int minConfirmations = 1,
    int minSeasonCoveragePercent = 0)
  {
    ArgumentNullException.ThrowIfNull(episodes);

    var result = new (int Start, int End)?[episodes.Count];
    for (var i = 0; i < episodes.Count; i++)
    {
      var confirmations = new List<(int Start, int End)>();
      for (var j = 0; j < episodes.Count; j++)
      {
        if (i == j)
        {
          continue;
        }

        var m = FindSharedRegion(episodes[i], episodes[j], maxBitDistance, maxGap, minRunFrames);
        if (m is FingerprintMatch region)
        {
          confirmations.Add((region.StartA, region.EndA));
        }
      }

      var agreeing = LargestAgreeingCluster(confirmations);
      if (agreeing.Count > 0 && agreeing.Count >= minConfirmations)
      {
        result[i] = Consensus(agreeing);
      }
    }

    return minSeasonCoveragePercent > 0 && !HasSeasonCoverage(result, minSeasonCoveragePercent)
      ? new (int Start, int End)?[episodes.Count]
      : result;
  }

  /// <summary>
  /// Whether enough of the season shares the sequence for it to be a real title/credits sequence rather
  /// than a coincidental match between a couple of episodes.
  /// </summary>
  /// <param name="regions">The per-episode regions (with <c>null</c> for "shares nothing").</param>
  /// <param name="minCoveragePercent">The percentage of episodes that must have a region.</param>
  /// <returns><c>true</c> when the season is covered well enough to keep its regions.</returns>
  internal static bool HasSeasonCoverage(IReadOnlyList<(int Start, int End)?> regions, int minCoveragePercent)
  {
    if (regions.Count == 0)
    {
      return false;
    }

    var found = 0;
    for (var i = 0; i < regions.Count; i++)
    {
      if (regions[i] is not null)
      {
        found++;
      }
    }

    return found * 100 >= minCoveragePercent * regions.Count;
  }

  // The biggest set of sibling matches that all point at the same place. Seeded on the region the most
  // others agree with, so a lone outlier can never drag the consensus away from the sequence itself.
  private static List<(int Start, int End)> LargestAgreeingCluster(List<(int Start, int End)> regions)
  {
    var best = new List<(int Start, int End)>();
    foreach (var seed in regions)
    {
      var cluster = new List<(int Start, int End)>();
      foreach (var other in regions)
      {
        if (Agree(seed, other))
        {
          cluster.Add(other);
        }
      }

      if (cluster.Count > best.Count)
      {
        best = cluster;
      }
    }

    return best;
  }

  // Two matches agree when they overlap over at least half of the shorter one — a genuine "same sequence",
  // not two regions that merely touch at an edge.
  private static bool Agree((int Start, int End) a, (int Start, int End) b)
  {
    var overlap = Math.Min(a.End, b.End) - Math.Max(a.Start, b.Start) + 1;
    if (overlap <= 0)
    {
      return false;
    }

    var shorter = Math.Min(a.End - a.Start + 1, b.End - b.Start + 1);
    return overlap * 2 >= shorter;
  }

  // Median start/end of the confirming regions — robust to an odd sibling whose run is a little longer or
  // shorter than the rest.
  private static (int Start, int End) Consensus(List<(int Start, int End)> regions)
  {
    var starts = new List<int>(regions.Count);
    var ends = new List<int>(regions.Count);
    foreach (var (start, end) in regions)
    {
      starts.Add(start);
      ends.Add(end);
    }

    starts.Sort();
    ends.Sort();
    return (starts[starts.Count / 2], ends[ends.Count / 2]);
  }

  // The most-voted alignment offsets (d = index in B minus index in A) from exact sub-fingerprint matches.
  private static List<int> TopOffsets(IReadOnlyList<uint> a, IReadOnlyList<uint> b)
  {
    var positions = new Dictionary<uint, List<int>>();
    for (var j = 0; j < b.Count; j++)
    {
      if (!positions.TryGetValue(b[j], out var list))
      {
        list = new List<int>();
        positions[b[j]] = list;
      }

      list.Add(j);
    }

    var votes = new Dictionary<int, int>();
    for (var i = 0; i < a.Count; i++)
    {
      if (positions.TryGetValue(a[i], out var js))
      {
        foreach (var j in js)
        {
          var d = j - i;
          votes[d] = votes.TryGetValue(d, out var c) ? c + 1 : 1;
        }
      }
    }

    var offsets = new List<int>(votes.Keys);
    offsets.Sort((x, y) => votes[y].CompareTo(votes[x]));
    if (offsets.Count > OffsetsToTry)
    {
      offsets.RemoveRange(OffsetsToTry, offsets.Count - OffsetsToTry);
    }

    return offsets;
  }

  // Longest contiguous low-distance run at a fixed offset (b index = a index + offset).
  private static FingerprintMatch? LongestRun(IReadOnlyList<uint> a, IReadOnlyList<uint> b, int offset, int maxBitDistance, int maxGap)
  {
    FingerprintMatch? best = null;
    var n = a.Count;
    var i = Math.Max(0, -offset);
    while (i < n && i + offset < b.Count)
    {
      if (BitOperations.PopCount(a[i] ^ b[i + offset]) <= maxBitDistance)
      {
        var start = i;
        var end = i;
        var gap = 0;
        var j = i;
        while (j < n && j + offset < b.Count)
        {
          if (BitOperations.PopCount(a[j] ^ b[j + offset]) <= maxBitDistance)
          {
            end = j;
            gap = 0;
          }
          else if (++gap > maxGap)
          {
            break;
          }

          j++;
        }

        var length = end - start + 1;
        if (best is null || length > best.Value.Length)
        {
          best = new FingerprintMatch(length, start, end, start + offset, end + offset);
        }

        i = j + 1;
      }
      else
      {
        i++;
      }
    }

    return best;
  }
}
