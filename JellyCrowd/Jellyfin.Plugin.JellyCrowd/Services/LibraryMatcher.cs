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
  public long GetSizeBytes(string mediaType, int tmdbId)
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
      total += kind.Value == BaseItemKind.Series ? SumEpisodeSizes(item) : item.Size ?? 0;
    }

    return total;
  }

  /// <inheritdoc />
  public IReadOnlyList<LibraryMediaItem> ListLibraryMedia()
  {
    var result = new List<LibraryMediaItem>();
    var kinds = new[] { (Kind: BaseItemKind.Movie, MediaType: "movie"), (Kind: BaseItemKind.Series, MediaType: "tv") };
    foreach (var (kind, mediaType) in kinds)
    {
      var items = _libraryManager.GetItemList(new InternalItemsQuery
      {
        IncludeItemTypes = new[] { kind },
        Recursive = true
      });

      foreach (var item in items)
      {
        var tmdb = item.GetProviderId(MetadataProvider.Tmdb);
        if (string.IsNullOrEmpty(tmdb) || !int.TryParse(tmdb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
        {
          continue;
        }

        result.Add(new LibraryMediaItem
        {
          JellyfinItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
          TmdbId = tmdbId,
          MediaType = mediaType,
          Title = item.Name ?? string.Empty,
          SizeBytes = kind == BaseItemKind.Series ? SumEpisodeSizes(item) : item.Size ?? 0
        });
      }
    }

    return result;
  }

  private long SumEpisodeSizes(BaseItem series)
  {
    var episodes = _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Episode },
      AncestorIds = new[] { series.Id },
      Recursive = true
    });

    long total = 0;
    foreach (var episode in episodes)
    {
      total += episode.Size ?? 0;
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
