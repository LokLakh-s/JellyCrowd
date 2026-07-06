using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="ILibraryMatcher"/> backed by <see cref="ILibraryManager"/>, matching on the
/// TMDB provider id.
/// </summary>
public sealed class LibraryMatcher : ILibraryMatcher
{
  private readonly ILibraryManager _libraryManager;

  /// <summary>
  /// Initializes a new instance of the <see cref="LibraryMatcher"/> class.
  /// </summary>
  /// <param name="libraryManager">The Jellyfin library manager.</param>
  public LibraryMatcher(ILibraryManager libraryManager)
  {
    _libraryManager = libraryManager;
  }

  /// <inheritdoc />
  public bool Exists(string mediaType, int tmdbId) => FindItemId(mediaType, tmdbId) is not null;

  /// <inheritdoc />
  public string? FindItemId(string mediaType, int tmdbId)
  {
    var kind = MediaTypeToKind(mediaType);
    if (kind is null)
    {
      return null;
    }

    var query = new InternalItemsQuery
    {
      IncludeItemTypes = new[] { kind.Value },
      HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        [MetadataProvider.Tmdb.ToString()] = tmdbId.ToString(CultureInfo.InvariantCulture)
      },
      Recursive = true,
      Limit = 1
    };

    var items = _libraryManager.GetItemList(query);
    return items.Count > 0 ? items[0].Id.ToString("N", CultureInfo.InvariantCulture) : null;
  }

  /// <inheritdoc />
  public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode)
  {
    // A whole-show request: the series existing is enough.
    if (season is null)
    {
      return FindItemId("tv", seriesTmdbId);
    }

    var series = _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Series },
      HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        [MetadataProvider.Tmdb.ToString()] = seriesTmdbId.ToString(CultureInfo.InvariantCulture)
      },
      Recursive = true,
      Limit = 1
    });

    if (series.Count == 0)
    {
      return null;
    }

    // Match the exact episode, or — when no episode is given — any episode of that season.
    var query = new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Episode },
      AncestorIds = new[] { series[0].Id },
      ParentIndexNumber = season,
      Recursive = true,
      Limit = 1
    };
    if (episode is not null)
    {
      query.IndexNumber = episode;
    }

    var episodes = _libraryManager.GetItemList(query);
    return episodes.Count > 0 ? episodes[0].Id.ToString("N", CultureInfo.InvariantCulture) : null;
  }

  /// <inheritdoc />
  public long GetSizeBytes(string mediaType, int tmdbId) => GetSizeBytes(mediaType, tmdbId, null, null);

  /// <inheritdoc />
  public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode)
  {
    var kind = MediaTypeToKind(mediaType);
    if (kind is null)
    {
      return 0;
    }

    var matches = _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { kind.Value },
      HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        [MetadataProvider.Tmdb.ToString()] = tmdbId.ToString(CultureInfo.InvariantCulture)
      },
      Recursive = true
    });

    long total = 0;
    foreach (var item in matches)
    {
      // For shows, count only the requested season/episode's files (so quota frees the right space on
      // a per-season/episode deletion); a whole-series request (season == null) sums everything.
      total += kind.Value == BaseItemKind.Series ? SumEpisodeSizes(item, season, episode) : item.Size ?? 0;
    }

    return total;
  }

  /// <inheritdoc />
  public string? FindSeasonItemId(int seriesTmdbId, int season)
  {
    var series = _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Series },
      HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        [MetadataProvider.Tmdb.ToString()] = seriesTmdbId.ToString(CultureInfo.InvariantCulture)
      },
      Recursive = true,
      Limit = 1
    });

    if (series.Count == 0)
    {
      return null;
    }

    var seasons = _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Season },
      AncestorIds = new[] { series[0].Id },
      Recursive = true
    });

    foreach (var item in seasons)
    {
      if (item.IndexNumber == season)
      {
        return item.Id.ToString("N", CultureInfo.InvariantCulture);
      }
    }

    return null;
  }

  /// <inheritdoc />
  public IReadOnlyList<LibraryMediaItem> ListLibraryMedia()
  {
    var result = new List<LibraryMediaItem>();

    // Movies: one entry each.
    foreach (var movie in _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Movie }, Recursive = true }))
    {
      if (!TryGetTmdbId(movie, out var tmdbId))
      {
        continue;
      }

      result.Add(new LibraryMediaItem
      {
        JellyfinItemId = movie.Id.ToString("N", CultureInfo.InvariantCulture),
        TmdbId = tmdbId,
        MediaType = "movie",
        Title = movie.Name ?? string.Empty,
        SizeBytes = movie.Size ?? 0
      });
    }

    // Shows: one entry per season, since ownership is per season — so a season owned by nobody surfaces as
    // its own orphan and is deleted on its own. A series with no season items falls back to one entry.
    foreach (var series in _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Series }, Recursive = true }))
    {
      if (!TryGetTmdbId(series, out var tmdbId))
      {
        continue;
      }

      var seasons = _libraryManager.GetItemList(new InternalItemsQuery
      {
        IncludeItemTypes = new[] { BaseItemKind.Season },
        AncestorIds = new[] { series.Id },
        Recursive = true
      });

      var emitted = 0;
      foreach (var season in seasons)
      {
        if (season.IndexNumber is not int seasonNumber)
        {
          continue;
        }

        result.Add(new LibraryMediaItem
        {
          JellyfinItemId = season.Id.ToString("N", CultureInfo.InvariantCulture),
          TmdbId = tmdbId,
          MediaType = "tv",
          Season = seasonNumber,
          Title = series.Name ?? string.Empty,
          SizeBytes = SumEpisodeSizes(series, seasonNumber)
        });
        emitted++;
      }

      if (emitted == 0)
      {
        result.Add(new LibraryMediaItem
        {
          JellyfinItemId = series.Id.ToString("N", CultureInfo.InvariantCulture),
          TmdbId = tmdbId,
          MediaType = "tv",
          Title = series.Name ?? string.Empty,
          SizeBytes = SumEpisodeSizes(series)
        });
      }
    }

    return result;
  }

  private static bool TryGetTmdbId(BaseItem item, out int tmdbId)
  {
    tmdbId = 0;
    var tmdb = item.GetProviderId(MetadataProvider.Tmdb);
    return !string.IsNullOrEmpty(tmdb)
      && int.TryParse(tmdb, NumberStyles.Integer, CultureInfo.InvariantCulture, out tmdbId);
  }

  private long SumEpisodeSizes(BaseItem series, int? season = null, int? episode = null)
  {
    var query = new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Episode },
      AncestorIds = new[] { series.Id },
      Recursive = true
    };
    if (season is not null)
    {
      query.ParentIndexNumber = season;
    }

    if (episode is not null)
    {
      query.IndexNumber = episode;
    }

    var episodes = _libraryManager.GetItemList(query);

    long total = 0;
    foreach (var item in episodes)
    {
      total += item.Size ?? 0;
    }

    return total;
  }

  /// <summary>
  /// Maps a Jelly Crowd media type to the corresponding Jellyfin item kind.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <returns>The matching <see cref="BaseItemKind"/>, or <c>null</c> if unsupported.</returns>
  public static BaseItemKind? MediaTypeToKind(string mediaType)
  {
    if (string.Equals(mediaType, "movie", StringComparison.Ordinal))
    {
      return BaseItemKind.Movie;
    }

    if (string.Equals(mediaType, "tv", StringComparison.Ordinal))
    {
      return BaseItemKind.Series;
    }

    return null;
  }
}
