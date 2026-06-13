using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
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
