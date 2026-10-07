using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// Where Sonarr (TVDB) files a TMDB show that TMDB splits differently: TMDB lists "Berlin and the Lady with an
/// Ermine" as a show of its own, TVDB as season 2 of "Berlin (2023)". Requests keep TMDB's numbering; every
/// exchange with Sonarr and every lookup in the library goes through this mapping. Built only from matching air
/// dates (see <c>SeasonAlignment</c>).
/// </summary>
public class SeriesMapping
{
  /// <summary>
  /// Gets or sets the TMDB show the requests are made for.
  /// </summary>
  public int TmdbId { get; set; }

  /// <summary>
  /// Gets or sets the TVDB series Sonarr files it under.
  /// </summary>
  public int TvdbId { get; set; }

  /// <summary>
  /// Gets or sets the title of that TVDB series, for the logs and the admin.
  /// </summary>
  public string? TvdbTitle { get; set; }

  /// <summary>
  /// Gets or sets which Sonarr season holds each TMDB season (episode numbers are the same on both sides).
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Deserialized from the mapping store.")]
  public Collection<SeasonLink> Seasons { get; set; } = new();

  /// <summary>
  /// Gets or sets when the mapping was established (UTC).
  /// </summary>
  public DateTime CreatedAt { get; set; }

  /// <summary>
  /// Gets the Sonarr season holding a TMDB season, or <c>null</c> when the mapping does not cover it.
  /// </summary>
  /// <param name="tmdbSeason">The TMDB season number.</param>
  /// <returns>The Sonarr season number, or <c>null</c>.</returns>
  public int? ToSonarr(int tmdbSeason) => Seasons.FirstOrDefault(s => s.TmdbSeason == tmdbSeason)?.SonarrSeason;

  /// <summary>
  /// Gets the Sonarr season a request's scope stands for: its season mapped, or — for a whole show — the one
  /// season the mapping holds.
  /// </summary>
  /// <param name="tmdbSeason">The request's TMDB season, or <c>null</c> for the whole show.</param>
  /// <returns>The Sonarr season, or <c>null</c> when the mapping does not hold the scope as a single season.</returns>
  public int? ToSonarrScope(int? tmdbSeason)
    => tmdbSeason is int season ? ToSonarr(season) : Seasons.Count == 1 ? Seasons[0].SonarrSeason : null;

  /// <summary>
  /// Gets the TMDB season a Sonarr season stands for, or <c>null</c> when the mapping does not cover it.
  /// </summary>
  /// <param name="sonarrSeason">The Sonarr (TVDB) season number.</param>
  /// <returns>The TMDB season number, or <c>null</c>.</returns>
  public int? ToTmdb(int sonarrSeason) => Seasons.FirstOrDefault(s => s.SonarrSeason == sonarrSeason)?.TmdbSeason;
}
