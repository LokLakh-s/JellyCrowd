using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure rules behind parental filtering: how a user's Jellyfin parental control and child group combine,
/// which of a title's ratings count, and whether a rating score is allowed. The allow rule is Jellyfin's
/// own (<c>BaseItem.IsParentalAllowed</c>), so a title is judged in the catalog exactly as it would be once
/// in the library.
/// </summary>
public static class ContentRestrictionPolicy
{
  // TMDB's ratings are looked up in the server's metadata country first, then in the US — the fallback
  // Jellyfin's own rating lookup uses, and the country TMDB rates most consistently.
  private const string FallbackCountry = "US";

  /// <summary>
  /// Combines a user's Jellyfin parental control with their child group: the lower age limit wins. A child
  /// account also never sees a title without a recognised rating, since nothing says it suits their age.
  /// </summary>
  /// <param name="maxParentalRating">The Jellyfin maximum parental rating score, or <c>null</c> for none.</param>
  /// <param name="maxParentalSubRating">The Jellyfin maximum parental rating sub-score, or <c>null</c>.</param>
  /// <param name="blockUnratedMovies">Whether Jellyfin blocks unrated movies for the user.</param>
  /// <param name="blockUnratedShows">Whether Jellyfin blocks unrated series for the user.</param>
  /// <param name="childMaxAge">The age tier of the user's child group, or <c>null</c> when not a child account.</param>
  /// <returns>The effective restriction.</returns>
  public static ContentRestriction Combine(
    int? maxParentalRating,
    int? maxParentalSubRating,
    bool blockUnratedMovies,
    bool blockUnratedShows,
    int? childMaxAge)
  {
    var childAge = childMaxAge.HasValue ? Math.Max(0, childMaxAge.Value) : (int?)null;
    int? maxScore = maxParentalRating;
    int? maxSubScore = maxParentalSubRating;
    if (childAge.HasValue && (!maxScore.HasValue || childAge.Value < maxScore.Value))
    {
      // The child tier is a plain age: any sub-score at that age is fine.
      maxScore = childAge;
      maxSubScore = null;
    }

    return new ContentRestriction
    {
      MaxScore = maxScore,
      MaxSubScore = maxScore.HasValue ? maxSubScore : null,
      BlockUnratedMovies = blockUnratedMovies || childAge.HasValue,
      BlockUnratedShows = blockUnratedShows || childAge.HasValue,
      ChildMaxAge = childAge,
    };
  }

  /// <summary>
  /// Picks the ratings that decide for a title: those of the server's metadata country, otherwise the US
  /// ones. A title rated only elsewhere counts as unrated, as it would in a Jellyfin library.
  /// </summary>
  /// <param name="ratingsByCountry">The title's ratings per country (upper-case codes).</param>
  /// <param name="country">The server's metadata country, or <c>null</c>.</param>
  /// <returns>The country the ratings come from and the ratings; empty ratings when there are none.</returns>
  public static (string Country, IReadOnlyList<string> Ratings) RatingsFor(
    IReadOnlyDictionary<string, IReadOnlyList<string>> ratingsByCountry,
    string? country)
  {
    ArgumentNullException.ThrowIfNull(ratingsByCountry);
    var code = string.IsNullOrWhiteSpace(country) ? FallbackCountry : country.Trim().ToUpperInvariant();
    if (ratingsByCountry.TryGetValue(code, out var ratings) && ratings.Count > 0)
    {
      return (code, ratings);
    }

    if (ratingsByCountry.TryGetValue(FallbackCountry, out var usRatings) && usRatings.Count > 0)
    {
      return (FallbackCountry, usRatings);
    }

    return (code, Array.Empty<string>());
  }

  /// <summary>
  /// Picks the strictest of a title's rating scores. A movie often carries several certifications in one
  /// country (one per release); judging it on the strictest never lets a cut through on its softest one.
  /// </summary>
  /// <param name="scores">The recognised scores (score, sub-score).</param>
  /// <returns>The strictest score, or <c>null</c> when there is none.</returns>
  public static (int Score, int? SubScore)? Strictest(IEnumerable<(int Score, int? SubScore)> scores)
  {
    ArgumentNullException.ThrowIfNull(scores);
    (int Score, int? SubScore)? strictest = null;
    foreach (var s in scores)
    {
      if (strictest is not { } current
          || s.Score > current.Score
          || (s.Score == current.Score && (s.SubScore ?? 0) > (current.SubScore ?? 0)))
      {
        strictest = s;
      }
    }

    return strictest;
  }

  /// <summary>
  /// Whether a title with the given rating score is allowed — Jellyfin's rule: an unrated title is allowed
  /// unless unrated titles of its kind are blocked; otherwise a lower score is allowed, a higher one is not,
  /// and at the limit itself the sub-score decides.
  /// </summary>
  /// <param name="restriction">The user's restriction.</param>
  /// <param name="score">The title's rating score, or <c>null</c> when it has no recognised rating.</param>
  /// <param name="subScore">The title's rating sub-score, if any.</param>
  /// <param name="isMovie">Whether the title is a movie (otherwise a show).</param>
  /// <returns><c>true</c> when the title is allowed.</returns>
  public static bool IsAllowed(ContentRestriction restriction, int? score, int? subScore, bool isMovie)
  {
    ArgumentNullException.ThrowIfNull(restriction);
    if (!score.HasValue)
    {
      return !(isMovie ? restriction.BlockUnratedMovies : restriction.BlockUnratedShows);
    }

    if (!restriction.MaxScore.HasValue)
    {
      return true;
    }

    if (score.Value != restriction.MaxScore.Value)
    {
      return score.Value < restriction.MaxScore.Value;
    }

    return !restriction.MaxSubScore.HasValue || (subScore ?? 0) <= restriction.MaxSubScore.Value;
  }
}
