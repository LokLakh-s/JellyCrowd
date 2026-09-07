using System;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// What a request covers on disk. Requests are stored at the granularity they were made, so the very same
/// files can be claimed by several of them: a whole-series request contains every season, and a season
/// request contains every episode of it. Left unmodelled, that overlap billed the same bytes to a user
/// several times over and sent the same download to the *arr backend once per request.
/// </summary>
/// <param name="MediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
/// <param name="TmdbId">The TMDB id of the title.</param>
/// <param name="Season">The season, or <c>null</c> for a whole series.</param>
/// <param name="Episode">The episode, or <c>null</c> for a whole season/series.</param>
public readonly record struct RequestScope(string? MediaType, int TmdbId, int? Season, int? Episode)
{
  /// <summary>
  /// Builds the scope a stored request covers.
  /// </summary>
  /// <param name="record">The request.</param>
  /// <returns>Its scope.</returns>
  public static RequestScope Of(RequestRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);
    return new RequestScope(record.MediaType, record.TmdbId, record.Season, record.Episode);
  }

  /// <summary>
  /// Whether this scope contains <paramref name="other"/> — the same title at an equal or narrower
  /// granularity. Containment is reflexive: a scope contains itself, so an exact duplicate is covered too.
  /// </summary>
  /// <param name="other">The scope to test.</param>
  /// <returns><c>true</c> when everything <paramref name="other"/> covers is already covered by this one.</returns>
  public bool Contains(RequestScope other)
  {
    if (TmdbId != other.TmdbId || !string.Equals(MediaType, other.MediaType, StringComparison.Ordinal))
    {
      return false;
    }

    // A whole series covers every season and episode of it.
    if (Season is null)
    {
      return true;
    }

    if (Season != other.Season)
    {
      return false;
    }

    // A whole season covers each of its episodes.
    return Episode is null || Episode == other.Episode;
  }
}
