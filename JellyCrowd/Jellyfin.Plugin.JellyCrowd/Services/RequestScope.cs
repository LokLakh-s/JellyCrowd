using System;
using System.Collections.Generic;
using System.Linq;
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
  /// Lists what the other active requests for the same title still cover: every request but the given one
  /// that is neither denied nor flagged for deletion — pending ones included, since they will want the media
  /// once approved. What a cancellation or a deletion takes away from the backend must stay out of these.
  /// </summary>
  /// <param name="all">Every stored request.</param>
  /// <param name="request">The request being withdrawn.</param>
  /// <returns>The scopes still wanted by others.</returns>
  public static IReadOnlyList<RequestScope> StillWantedByOthers(IEnumerable<RequestRecord> all, RequestRecord request)
  {
    ArgumentNullException.ThrowIfNull(all);
    ArgumentNullException.ThrowIfNull(request);
    return all
      .Where(r => r.Id != request.Id
        && r.TmdbId == request.TmdbId
        && string.Equals(r.MediaType, request.MediaType, StringComparison.Ordinal)
        && r.Status != RequestStatus.Denied
        && r.DeletionRequestedAt is null)
      .Select(Of)
      .ToList();
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
