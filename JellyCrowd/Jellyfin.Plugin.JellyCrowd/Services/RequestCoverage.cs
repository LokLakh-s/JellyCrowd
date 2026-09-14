using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Decides, episode by episode, what a season or series request adds for a user. Comparing request scopes
/// alone refused to complete a partly owned show (a season owned blocked the whole series) and could never
/// see that a fulfilled season was missing an episode. Pure, so the rule is unit-tested.
/// </summary>
public static class RequestCoverage
{
  /// <summary>
  /// Evaluates a prospective request against what is in the library and what the user already asked for.
  /// It is already covered when every episode is either on its way through one of the user's requests, or
  /// owned by the user and present. It reserves only the episodes that are neither on disk nor on their
  /// way — so completing a show costs the missing part, not the whole of it.
  /// </summary>
  /// <param name="wanted">Every episode the requested scope covers.</param>
  /// <param name="present">Episodes of the title currently in the library, whoever owns them.</param>
  /// <param name="coveredInFlight">Episodes covered by the user's own requests that are still in flight.</param>
  /// <param name="coveredOwned">Episodes covered by the user's own fulfilled requests.</param>
  /// <returns>Whether the request adds nothing, and how many episodes it must reserve.</returns>
  public static CoverageDecision Evaluate(
    IReadOnlyCollection<EpisodeKey> wanted,
    IReadOnlyCollection<EpisodeKey> present,
    IReadOnlyCollection<EpisodeKey> coveredInFlight,
    IReadOnlyCollection<EpisodeKey> coveredOwned)
  {
    ArgumentNullException.ThrowIfNull(wanted);
    var onDisk = ToSet(present);
    var coming = ToSet(coveredInFlight);
    var owned = ToSet(coveredOwned);

    var alreadyCovered = wanted.Count > 0;
    var toReserve = 0;
    foreach (var key in wanted)
    {
      var inFlight = coming.Contains(key);
      if (!inFlight && !(owned.Contains(key) && onDisk.Contains(key)))
      {
        alreadyCovered = false;
      }

      if (!inFlight && !onDisk.Contains(key))
      {
        toReserve++;
      }
    }

    return new CoverageDecision(alreadyCovered, toReserve);
  }

  private static HashSet<EpisodeKey> ToSet(IReadOnlyCollection<EpisodeKey>? keys)
    => keys as HashSet<EpisodeKey> ?? new HashSet<EpisodeKey>(keys ?? Array.Empty<EpisodeKey>());
}
