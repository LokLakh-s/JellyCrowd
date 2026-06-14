using System;
using System.Globalization;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure builder turning a stored <see cref="RequestRecord"/> into the backend-agnostic
/// <see cref="DownloadDispatch"/> payload. Kept free of I/O so it can be unit-tested directly.
/// </summary>
public static class DownloadPayloadBuilder
{
  /// <summary>
  /// Builds a <see cref="DownloadDispatch"/> from a request and the resolved requester name.
  /// </summary>
  /// <param name="request">The source request.</param>
  /// <param name="userName">The resolved requesting-user display name.</param>
  /// <returns>The dispatch payload.</returns>
  public static DownloadDispatch Build(RequestRecord request, string userName)
  {
    ArgumentNullException.ThrowIfNull(request);

    return new DownloadDispatch
    {
      RequestId = request.Id,
      UserId = request.UserId,
      UserName = userName ?? string.Empty,
      TmdbId = request.TmdbId,
      MediaType = request.MediaType,
      Title = request.Title,
      Year = ParseYear(request.ReleaseDate),
      ReleaseDate = request.ReleaseDate,
      Season = request.Season,
      Episode = request.Episode,
      PosterPath = request.PosterPath,
      RequestedAt = request.RequestedAt,
      DesiredAt = request.DesiredAt,
      TmdbUrl = "https://www.themoviedb.org/" + request.MediaType + "/" + request.TmdbId.ToString(CultureInfo.InvariantCulture)
    };
  }

  /// <summary>
  /// Extracts the 4-digit year from a TMDB date string (<c>YYYY-MM-DD</c>), or <c>null</c>.
  /// </summary>
  /// <param name="releaseDate">The release date string.</param>
  /// <returns>The year, or <c>null</c> when absent/unparseable.</returns>
  public static int? ParseYear(string? releaseDate)
  {
    if (string.IsNullOrWhiteSpace(releaseDate) || releaseDate.Length < 4)
    {
      return null;
    }

    return int.TryParse(releaseDate.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
      ? year
      : null;
  }
}
