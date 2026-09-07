using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Reduces a user's fulfilled requests to the ones that own distinct files, so the quota bills each byte
/// once. A whole-series claim already covers its seasons and a season claim already covers its episodes:
/// summing every claim's size charged the same episodes several times over, and could put a user over
/// their quota on content that fits.
/// </summary>
public static class QuotaClaims
{
  /// <summary>
  /// Drops the claims another claim already covers, plus exact duplicates, keeping the broadest claim of
  /// each overlapping set — its size already accounts for everything the narrower ones point at.
  /// </summary>
  /// <param name="claims">The user's fulfilled requests, in any order.</param>
  /// <returns>The claims to bill, in input order.</returns>
  public static IReadOnlyList<RequestRecord> Deduplicate(IReadOnlyList<RequestRecord> claims)
  {
    if (claims is null || claims.Count == 0)
    {
      return System.Array.Empty<RequestRecord>();
    }

    var scopes = new RequestScope[claims.Count];
    for (var i = 0; i < claims.Count; i++)
    {
      scopes[i] = RequestScope.Of(claims[i]);
    }

    var kept = new List<RequestRecord>(claims.Count);
    var seen = new HashSet<RequestScope>();
    for (var i = 0; i < claims.Count; i++)
    {
      var covered = false;
      for (var j = 0; j < claims.Count; j++)
      {
        // Strictly broader: it contains this claim, and this claim does not contain it back (mutual
        // containment just means they are the same scope, which the seen-set settles instead).
        if (i != j && scopes[j].Contains(scopes[i]) && !scopes[i].Contains(scopes[j]))
        {
          covered = true;
          break;
        }
      }

      if (!covered && seen.Add(scopes[i]))
      {
        kept.Add(claims[i]);
      }
    }

    return kept;
  }
}
