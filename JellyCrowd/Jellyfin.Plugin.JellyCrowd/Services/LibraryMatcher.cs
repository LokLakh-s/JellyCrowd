using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="ILibraryMatcher"/> backed by <see cref="ILibraryManager"/>, matching on the
/// TMDB provider id — or, for a TMDB show Sonarr files under another series (see <see cref="SeriesMapping"/>),
/// on that series' TVDB id and the seasons the mapping names. Seasons and episodes are always given and
/// returned in TMDB's numbering.
/// </summary>
public sealed class LibraryMatcher : ILibraryMatcher
{
  private readonly ILibraryManager _libraryManager;
  private readonly ISeriesMappingStore? _mappings;

  /// <summary>
  /// Initializes a new instance of the <see cref="LibraryMatcher"/> class.
  /// </summary>
  /// <param name="libraryManager">The Jellyfin library manager.</param>
  /// <param name="mappings">The TMDB → Sonarr series mappings, if any.</param>
  public LibraryMatcher(ILibraryManager libraryManager, ISeriesMappingStore? mappings = null)
  {
    _libraryManager = libraryManager;
    _mappings = mappings;
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

    if (kind.Value == BaseItemKind.Movie)
    {
      var movies = _libraryManager.GetItemList(ByProvider(BaseItemKind.Movie, MetadataProvider.Tmdb, tmdbId, null));
      return movies.Count > 0 ? movies[0].Id.ToString("N", CultureInfo.InvariantCulture) : null;
    }

    var show = FindShow(tmdbId, null);
    if (show is null)
    {
      return null;
    }

    // A series only counts once it holds an episode: Jellyfin keeps the series and its season folders after
    // their files are deleted, and that empty shell is neither available nor something to own.
    foreach (var series in show.Series)
    {
      if (!show.Restricted)
      {
        if (HasEpisode(series))
        {
          return Id(series);
        }

        continue;
      }

      if (!HasEpisode(series, show))
      {
        continue;
      }

      // A show mapped onto a single season of another series is that season: deleting "the show" must not
      // take the rest of the series with it.
      if (show.Mapping is { Seasons.Count: 1 } single
          && FindSeason(series, single.Seasons[0].SonarrSeason) is { } season)
      {
        return Id(season);
      }

      return Id(series);
    }

    return null;
  }

  /// <inheritdoc />
  public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode)
  {
    // A whole-show request: the series existing is enough.
    if (season is null)
    {
      return FindItemId("tv", seriesTmdbId);
    }

    var show = FindShow(seriesTmdbId, 1);
    if (show is null || show.LibrarySeason(season.Value) is not int librarySeason)
    {
      return null;
    }

    // Match the exact episode, or — when no episode is given — any episode of that season.
    var query = new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Episode },
      AncestorIds = new[] { show.Series[0].Id },
      ParentIndexNumber = librarySeason,
      Recursive = true,
      Limit = 1
    };
    if (episode is not null)
    {
      query.IndexNumber = episode;
    }

    var episodes = _libraryManager.GetItemList(query);
    return episodes.Count > 0 ? Id(episodes[0]) : null;
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

    if (kind.Value == BaseItemKind.Movie)
    {
      long movies = 0;
      foreach (var item in _libraryManager.GetItemList(ByProvider(BaseItemKind.Movie, MetadataProvider.Tmdb, tmdbId, null)))
      {
        movies += item.Size ?? 0;
      }

      return movies;
    }

    var show = FindShow(tmdbId, null);
    if (show is null)
    {
      return 0;
    }

    // For shows, count only the requested season/episode's files (so quota frees the right space on
    // a per-season/episode deletion); a whole-series request (season == null) sums everything that is the show's.
    long total = 0;
    foreach (var series in show.Series)
    {
      if (season is int tmdbSeason)
      {
        total += show.LibrarySeason(tmdbSeason) is int librarySeason ? SumEpisodeSizes(series, librarySeason, episode) : 0;
      }
      else
      {
        total += show.Restricted
          ? Episodes(series, null).Where(e => e.ParentIndexNumber is int s && show.TmdbSeason(s) is not null).Sum(e => e.Size ?? 0)
          : SumEpisodeSizes(series);
      }
    }

    return total;
  }

  /// <inheritdoc />
  public string? FindSeasonItemId(int seriesTmdbId, int season)
  {
    var show = FindShow(seriesTmdbId, 1);
    if (show is null || show.LibrarySeason(season) is not int librarySeason)
    {
      return null;
    }

    return FindSeason(show.Series[0], librarySeason) is { } item ? Id(item) : null;
  }

  /// <inheritdoc />
  public IReadOnlyList<LibraryMediaItem> ListLibraryMedia()
  {
    var result = new List<LibraryMediaItem>();

    // Movies: one entry each.
    foreach (var movie in _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Movie }, Recursive = true }))
    {
      if (!TryGetProviderId(movie, MetadataProvider.Tmdb, out var tmdbId))
      {
        continue;
      }

      result.Add(new LibraryMediaItem
      {
        JellyfinItemId = Id(movie),
        TmdbId = tmdbId,
        MediaType = "movie",
        Title = movie.Name ?? string.Empty,
        SizeBytes = movie.Size ?? 0
      });
    }

    // Shows: one entry per season, since ownership is per season — so a season owned by nobody surfaces as
    // its own orphan and is deleted on its own. A series with no season items falls back to one entry. A
    // season another TMDB show is mapped onto is listed as that show's.
    foreach (var series in _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Series }, Recursive = true }))
    {
      var hasTmdb = TryGetProviderId(series, MetadataProvider.Tmdb, out var tmdbId);
      var mapped = MappingsOf(series);
      if (!hasTmdb && mapped.Count == 0)
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

        var owner = mapped.FirstOrDefault(m => m.ToTmdb(seasonNumber) is not null);
        if (owner is null && !hasTmdb)
        {
          continue;
        }

        result.Add(new LibraryMediaItem
        {
          JellyfinItemId = Id(season),
          TmdbId = owner?.TmdbId ?? tmdbId,
          MediaType = "tv",
          Season = owner?.ToTmdb(seasonNumber) ?? seasonNumber,
          Title = series.Name ?? string.Empty,
          SizeBytes = SumEpisodeSizes(series, seasonNumber)
        });
        emitted++;
      }

      if (emitted == 0 && hasTmdb)
      {
        result.Add(new LibraryMediaItem
        {
          JellyfinItemId = Id(series),
          TmdbId = tmdbId,
          MediaType = "tv",
          Title = series.Name ?? string.Empty,
          SizeBytes = SumEpisodeSizes(series)
        });
      }
    }

    return result;
  }

  /// <inheritdoc />
  public IReadOnlyCollection<EpisodeKey> ListEpisodeKeys(int seriesTmdbId, int? season)
  {
    var keys = new HashSet<EpisodeKey>();
    var show = FindShow(seriesTmdbId, 1);
    if (show is null)
    {
      return keys;
    }

    int? librarySeason = null;
    if (season is int tmdbSeason)
    {
      librarySeason = show.LibrarySeason(tmdbSeason);
      if (librarySeason is null)
      {
        return keys;
      }
    }

    foreach (var item in Episodes(show.Series[0], librarySeason))
    {
      // A "missing episode" placeholder has no file: counting it would make an incomplete season look done.
      if (!item.IsVirtualItem && item.ParentIndexNumber is int s && item.IndexNumber is int e && show.TmdbSeason(s) is int tmdb)
      {
        keys.Add(new EpisodeKey(tmdb, e));
      }
    }

    return keys;
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

  private static string Id(BaseItem item) => item.Id.ToString("N", CultureInfo.InvariantCulture);

  private static InternalItemsQuery ByProvider(BaseItemKind kind, MetadataProvider provider, int id, int? limit)
  {
    var query = new InternalItemsQuery
    {
      IncludeItemTypes = new[] { kind },
      HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        [provider.ToString()] = id.ToString(CultureInfo.InvariantCulture)
      },
      Recursive = true
    };
    if (limit is not null)
    {
      query.Limit = limit;
    }

    return query;
  }

  private static bool TryGetProviderId(BaseItem item, MetadataProvider provider, out int id)
  {
    id = 0;
    var value = item.GetProviderId(provider);
    return !string.IsNullOrEmpty(value)
      && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
  }

  // Where a TMDB show is in the library: the series its own TMDB id names, or — when Sonarr files it under
  // another series — that series, by its TVDB id. A copy filed under the show's own TMDB id (added by hand)
  // still wins when it holds an episode. Seasons other TMDB shows are mapped onto are not this one's.
  private Show? FindShow(int tmdbId, int? limit)
  {
    var mapping = _mappings?.Get(tmdbId);
    var series = _libraryManager.GetItemList(ByProvider(BaseItemKind.Series, MetadataProvider.Tmdb, tmdbId, limit));
    if (mapping is not null)
    {
      if (series.Count > 0 && series.Any(HasEpisode))
      {
        return new Show(series, null, new HashSet<int>());
      }

      series = _libraryManager.GetItemList(ByProvider(BaseItemKind.Series, MetadataProvider.Tvdb, mapping.TvdbId, limit));
    }

    if (series.Count == 0)
    {
      return null;
    }

    var claimed = new HashSet<int>();
    if (mapping is null)
    {
      foreach (var other in series.SelectMany(MappingsOf).Where(m => m.TmdbId != tmdbId))
      {
        claimed.UnionWith(other.Seasons.Select(s => s.SonarrSeason));
      }
    }

    return new Show(series, mapping, claimed);
  }

  // The mappings that point into a library series (by its TVDB id).
  private IReadOnlyList<SeriesMapping> MappingsOf(BaseItem series)
    => _mappings is not null && TryGetProviderId(series, MetadataProvider.Tvdb, out var tvdbId)
      ? _mappings.ForTvdb(tvdbId)
      : Array.Empty<SeriesMapping>();

  private BaseItem? FindSeason(BaseItem series, int librarySeason)
    => _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Season },
      AncestorIds = new[] { series.Id },
      Recursive = true
    }).FirstOrDefault(s => s.IndexNumber == librarySeason);

  private IReadOnlyList<BaseItem> Episodes(BaseItem series, int? librarySeason)
  {
    var query = new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Episode },
      AncestorIds = new[] { series.Id },
      Recursive = true
    };
    if (librarySeason is not null)
    {
      query.ParentIndexNumber = librarySeason;
    }

    return _libraryManager.GetItemList(query);
  }

  private bool HasEpisode(BaseItem series)
    => _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Episode },
      AncestorIds = new[] { series.Id },
      IsVirtualItem = false,
      Recursive = true,
      Limit = 1
    }).Count > 0;

  // Whether a series holds an episode file in one of the show's own seasons.
  private bool HasEpisode(BaseItem series, Show show)
    => _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Episode },
      AncestorIds = new[] { series.Id },
      IsVirtualItem = false,
      Recursive = true
    }).Any(e => e.ParentIndexNumber is int s && show.TmdbSeason(s) is not null);

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

  // A TMDB show as the library holds it: its series item(s), and which of their seasons are the show's.
  private sealed class Show
  {
    private readonly IReadOnlySet<int> _claimed;

    public Show(IReadOnlyList<BaseItem> series, SeriesMapping? mapping, IReadOnlySet<int> claimed)
    {
      Series = series;
      Mapping = mapping;
      _claimed = claimed;
    }

    public IReadOnlyList<BaseItem> Series { get; }

    public SeriesMapping? Mapping { get; }

    // Whether only some of the series' seasons are the show's.
    public bool Restricted => Mapping is not null || _claimed.Count > 0;

    // The library season holding a TMDB season, or null when that season is not the show's.
    public int? LibrarySeason(int tmdbSeason)
      => Mapping is not null ? Mapping.ToSonarr(tmdbSeason) : _claimed.Contains(tmdbSeason) ? null : tmdbSeason;

    // The TMDB season a library season stands for, or null when it is not the show's.
    public int? TmdbSeason(int librarySeason)
      => Mapping is not null ? Mapping.ToTmdb(librarySeason) : _claimed.Contains(librarySeason) ? null : librarySeason;
  }
}
