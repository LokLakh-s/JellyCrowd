using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Genres TMDB has for movies but not for shows (Horror, Thriller), offered on the TV tab anyway and
/// backed by a TMDB keyword, since <c>/discover/tv</c> finds nothing for the movie genre ids.
/// </summary>
/// <remarks>
/// Each one keeps its movie genre id, so it cannot collide with a TV genre id and means the same thing on
/// both tabs. Its label comes from the movie genre list, which TMDB localizes.
/// </remarks>
public static class TvKeywordGenres
{
  private static readonly Entry[] Entries =
  {
    new(27, 315058, "Horror"),
    new(53, 316362, "Thriller")
  };

  /// <summary>
  /// Adds the keyword-backed genres to the TV genre list, sorted by name with the rest.
  /// </summary>
  /// <param name="tvGenres">The TV genres TMDB returned.</param>
  /// <param name="movieGenres">The movie genres in the same language (labels); may be empty.</param>
  /// <returns>The TV genres plus the ones TMDB lacks.</returns>
  public static IReadOnlyList<Genre> Merge(IReadOnlyList<Genre> tvGenres, IReadOnlyList<Genre> movieGenres)
  {
    ArgumentNullException.ThrowIfNull(tvGenres);
    ArgumentNullException.ThrowIfNull(movieGenres);

    var merged = tvGenres.ToList();
    foreach (var entry in Entries)
    {
      var name = movieGenres.FirstOrDefault(g => g.Id == entry.GenreId)?.Name;
      if (string.IsNullOrWhiteSpace(name))
      {
        name = entry.FallbackName;
      }

      // Should TMDB ever add the genre to its TV list, keep its own entry rather than a duplicate.
      var alreadyListed = merged.Any(g => g.Id == entry.GenreId
        || string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
      if (!alreadyListed)
      {
        merged.Add(new Genre { Id = entry.GenreId, Name = name });
      }
    }

    var byName = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);
    return merged.OrderBy(g => g.Name, byName).ToList();
  }

  /// <summary>
  /// Splits a comma-separated TV genre filter into real TMDB genre ids and keyword ids. Both lists keep
  /// TMDB's comma meaning (all of them required).
  /// </summary>
  /// <param name="genres">The comma-separated genre ids from the catalog.</param>
  /// <returns>The <c>with_genres</c> and <c>with_keywords</c> values, each null when empty.</returns>
  public static (string? Genres, string? Keywords) Split(string? genres)
  {
    if (string.IsNullOrWhiteSpace(genres))
    {
      return (null, null);
    }

    var genreIds = new List<string>();
    var keywordIds = new List<string>();
    foreach (var raw in genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      var entry = Entries.FirstOrDefault(e => string.Equals(
        e.GenreId.ToString(CultureInfo.InvariantCulture), raw, StringComparison.Ordinal));
      if (entry is null)
      {
        genreIds.Add(raw);
      }
      else if (!keywordIds.Contains(entry.KeywordIdText))
      {
        keywordIds.Add(entry.KeywordIdText);
      }
    }

    return (
      genreIds.Count > 0 ? string.Join(',', genreIds) : null,
      keywordIds.Count > 0 ? string.Join(',', keywordIds) : null);
  }

  private sealed record Entry(int GenreId, int KeywordId, string FallbackName)
  {
    public string KeywordIdText => KeywordId.ToString(CultureInfo.InvariantCulture);
  }
}
