using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure mapping from a child group's age tier (0 = all ages, 10, 12, 16) to a TMDB movie certification
/// for the <c>certification.lte</c> discover filter, per country. Adult content is always excluded
/// separately (<c>include_adult=false</c>); this narrows the catalog further to age-appropriate ratings.
/// TV has no reliable TMDB certification filter, so this applies to movies only.
/// </summary>
public static class ChildContentPolicy
{
  // Per-country movie certification ladders, indexed by age tier. Values are TMDB certification strings.
  private static readonly Dictionary<string, Dictionary<int, string>> Ladders = new(StringComparer.OrdinalIgnoreCase)
  {
    ["US"] = new() { [0] = "G", [10] = "PG", [12] = "PG-13", [16] = "R" },
    ["GB"] = new() { [0] = "U", [10] = "PG", [12] = "12", [16] = "15" },
    ["FR"] = new() { [0] = "U", [10] = "10", [12] = "12", [16] = "16" },
    ["DE"] = new() { [0] = "0", [10] = "6", [12] = "12", [16] = "16" },
    ["ES"] = new() { [0] = "APTA", [10] = "7", [12] = "12", [16] = "16" },
    ["NL"] = new() { [0] = "AL", [10] = "6", [12] = "12", [16] = "16" },
    ["BR"] = new() { [0] = "L", [10] = "10", [12] = "12", [16] = "16" },
  };

  /// <summary>
  /// Resolves the TMDB certification country and the <c>certification.lte</c> value for the given country
  /// and age tier. Unknown countries fall back to the US rating ladder (widely populated in TMDB), so a
  /// real family filter still applies. Returns <c>null</c> only when the age tier is out of range.
  /// </summary>
  /// <param name="country">The two-letter country code (e.g. <c>FR</c>); <c>null</c>/empty falls back to US.</param>
  /// <param name="maxAge">The age tier (0, 10, 12 or 16); other values are bucketed to the nearest tier.</param>
  /// <returns>The certification country and the max certification, or <c>null</c>.</returns>
  public static (string Country, string Certification)? CertificationFor(string? country, int maxAge)
  {
    var tier = BucketTier(maxAge);
    var code = string.IsNullOrWhiteSpace(country) ? "US" : country.Trim().ToUpperInvariant();
    if (Ladders.TryGetValue(code, out var ladder) && ladder.TryGetValue(tier, out var cert))
    {
      return (code, cert);
    }

    // Unknown country → use the US ladder (certification_country=US) as a broadly-populated safety net.
    if (Ladders["US"].TryGetValue(tier, out var usCert))
    {
      return ("US", usCert);
    }

    return null;
  }

  // Snap an arbitrary age to one of the supported tiers (0, 10, 12, 16).
  private static int BucketTier(int maxAge)
  {
    if (maxAge <= 0)
    {
      return 0;
    }

    if (maxAge <= 10)
    {
      return 10;
    }

    if (maxAge <= 12)
    {
      return 12;
    }

    return 16;
  }
}
