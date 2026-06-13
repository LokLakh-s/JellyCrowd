using System;
using System.Globalization;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure helpers deciding when a request should be fulfilled. An unreleased title is automatically
/// scheduled for its release date so the download backend is only triggered once the media is out.
/// </summary>
public static class RequestScheduling
{
  /// <summary>
  /// Resolves the effective desired (UTC) fulfillment time: the later of the user's desired time
  /// (or "now" when unset) and the release date when that release is still in the future. This
  /// defers dispatch for not-yet-released movies/shows until they are out.
  /// </summary>
  /// <param name="releaseDate">The TMDB release/first-air date (<c>yyyy-MM-dd</c>), if known.</param>
  /// <param name="requestedDesiredUtc">The user's requested desired time, if any.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <returns>The effective desired UTC time.</returns>
  public static DateTime ResolveDesiredAt(string? releaseDate, DateTime? requestedDesiredUtc, DateTime nowUtc)
  {
    var baseDesired = requestedDesiredUtc ?? nowUtc;
    var release = ParseReleaseDate(releaseDate);
    if (release is { } r && r > nowUtc && r > baseDesired)
    {
      return r;
    }

    return baseDesired;
  }

  /// <summary>
  /// Parses a TMDB date string (<c>yyyy-MM-dd</c>) as UTC midnight, or returns <c>null</c>.
  /// </summary>
  /// <param name="releaseDate">The date string.</param>
  /// <returns>The parsed UTC instant, or <c>null</c> when absent/unparseable.</returns>
  public static DateTime? ParseReleaseDate(string? releaseDate)
  {
    if (string.IsNullOrWhiteSpace(releaseDate))
    {
      return null;
    }

    return DateTime.TryParse(
      releaseDate,
      CultureInfo.InvariantCulture,
      DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
      out var parsed)
      ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
      : null;
  }
}
