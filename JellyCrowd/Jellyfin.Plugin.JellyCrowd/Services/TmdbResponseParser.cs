using System;
using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure (network-free) parsing of TMDB JSON payloads into <see cref="CatalogItem"/> instances.
/// Kept separate from <see cref="TmdbClient"/> so it can be unit tested without HTTP.
/// </summary>
public static class TmdbResponseParser
{
  private const string MovieType = "movie";
  private const string TvType = "tv";

  /// <summary>
  /// Parses a TMDB list payload (a JSON object with a <c>results</c> array, e.g. trending or search).
  /// Person results and unknown media types are skipped.
  /// </summary>
  /// <param name="json">The raw TMDB JSON payload.</param>
  /// <param name="defaultMediaType">Media type to assume when the payload omits <c>media_type</c> (e.g. a movie-only search). May be <c>null</c>.</param>
  /// <returns>The parsed catalog items.</returns>
  public static IReadOnlyList<CatalogItem> ParseResults(string json, string? defaultMediaType = null)
  {
    ArgumentNullException.ThrowIfNull(json);

    var items = new List<CatalogItem>();
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
    {
      return items;
    }

    foreach (var element in results.EnumerateArray())
    {
      var item = ParseElement(element, defaultMediaType);
      if (item is not null)
      {
        items.Add(item);
      }
    }

    return items;
  }

  /// <summary>
  /// Parses a single TMDB detail payload (e.g. <c>/movie/{id}</c> or <c>/tv/{id}</c>).
  /// </summary>
  /// <param name="json">The raw TMDB JSON payload.</param>
  /// <param name="mediaType">The media type of the requested entity (<c>movie</c> or <c>tv</c>).</param>
  /// <returns>The parsed item, or <c>null</c> if the type is unsupported.</returns>
  public static CatalogItem? ParseDetails(string json, string mediaType)
  {
    ArgumentNullException.ThrowIfNull(json);

    using var doc = JsonDocument.Parse(json);
    return ParseElement(doc.RootElement, mediaType);
  }

  private static CatalogItem? ParseElement(JsonElement element, string? defaultMediaType)
  {
    var mediaType = GetString(element, "media_type") ?? defaultMediaType;
    if (!string.Equals(mediaType, MovieType, StringComparison.Ordinal)
        && !string.Equals(mediaType, TvType, StringComparison.Ordinal))
    {
      return null;
    }

    var isMovie = string.Equals(mediaType, MovieType, StringComparison.Ordinal);

    return new CatalogItem
    {
      TmdbId = GetInt(element, "id"),
      MediaType = mediaType!,
      Title = GetString(element, isMovie ? "title" : "name")
              ?? GetString(element, "title")
              ?? GetString(element, "name")
              ?? string.Empty,
      Overview = GetString(element, "overview"),
      PosterPath = GetString(element, "poster_path"),
      BackdropPath = GetString(element, "backdrop_path"),
      ReleaseDate = GetString(element, isMovie ? "release_date" : "first_air_date"),
      VoteAverage = GetDouble(element, "vote_average"),
      Genres = GetGenreNames(element),
      Runtime = GetRuntime(element, isMovie),
      ImdbId = GetImdbId(element)
    };
  }

  /// <summary>
  /// Parses a TMDB genre-list payload (<c>/genre/movie/list</c> or <c>/genre/tv/list</c>).
  /// </summary>
  /// <param name="json">The raw TMDB JSON payload.</param>
  /// <returns>The parsed genres.</returns>
  public static IReadOnlyList<Genre> ParseGenres(string json)
  {
    ArgumentNullException.ThrowIfNull(json);

    var genres = new List<Genre>();
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("genres", out var array) || array.ValueKind != JsonValueKind.Array)
    {
      return genres;
    }

    foreach (var element in array.EnumerateArray())
    {
      var name = GetString(element, "name");
      if (name is not null)
      {
        genres.Add(new Genre { Id = GetInt(element, "id"), Name = name });
      }
    }

    return genres;
  }

  /// <summary>
  /// Parses the seasons from a TMDB show detail payload (<c>/tv/{id}</c>).
  /// </summary>
  /// <param name="json">The raw TMDB JSON payload.</param>
  /// <returns>The parsed seasons.</returns>
  public static IReadOnlyList<Season> ParseSeasons(string json)
  {
    ArgumentNullException.ThrowIfNull(json);

    var seasons = new List<Season>();
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("seasons", out var array) || array.ValueKind != JsonValueKind.Array)
    {
      return seasons;
    }

    foreach (var element in array.EnumerateArray())
    {
      seasons.Add(new Season
      {
        SeasonNumber = GetInt(element, "season_number"),
        Name = GetString(element, "name") ?? string.Empty,
        EpisodeCount = GetInt(element, "episode_count")
      });
    }

    return seasons;
  }

  /// <summary>
  /// Parses a TMDB watch-providers payload (<c>/watch/providers/{movie|tv}?watch_region=…</c>), sorted by
  /// regional display priority.
  /// </summary>
  /// <param name="json">The raw TMDB JSON payload.</param>
  /// <returns>The parsed providers, most prominent first.</returns>
  public static IReadOnlyList<WatchProvider> ParseWatchProviders(string json)
  {
    ArgumentNullException.ThrowIfNull(json);

    var providers = new List<WatchProvider>();
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("results", out var array) || array.ValueKind != JsonValueKind.Array)
    {
      return providers;
    }

    foreach (var element in array.EnumerateArray())
    {
      var name = GetString(element, "provider_name");
      if (name is null)
      {
        continue;
      }

      providers.Add(new WatchProvider
      {
        Id = GetInt(element, "provider_id"),
        Name = name,
        LogoPath = GetString(element, "logo_path"),
        DisplayPriority = GetInt(element, "display_priority")
      });
    }

    providers.Sort((a, b) => a.DisplayPriority.CompareTo(b.DisplayPriority));
    return providers;
  }

  private static IReadOnlyList<string> GetGenreNames(JsonElement element)
  {
    if (!element.TryGetProperty("genres", out var array) || array.ValueKind != JsonValueKind.Array)
    {
      return Array.Empty<string>();
    }

    var names = new List<string>();
    foreach (var genre in array.EnumerateArray())
    {
      var name = GetString(genre, "name");
      if (name is not null)
      {
        names.Add(name);
      }
    }

    return names;
  }

  private static int? GetRuntime(JsonElement element, bool isMovie)
  {
    if (isMovie)
    {
      var runtime = GetInt(element, "runtime");
      return runtime > 0 ? runtime : null;
    }

    if (element.TryGetProperty("episode_run_time", out var array)
        && array.ValueKind == JsonValueKind.Array)
    {
      foreach (var value in array.EnumerateArray())
      {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var minutes) && minutes > 0)
        {
          return minutes;
        }
      }
    }

    return null;
  }

  private static string? GetImdbId(JsonElement element)
  {
    var direct = GetString(element, "imdb_id");
    if (direct is not null)
    {
      return direct;
    }

    if (element.TryGetProperty("external_ids", out var external) && external.ValueKind == JsonValueKind.Object)
    {
      return GetString(external, "imdb_id");
    }

    return null;
  }

  private static string? GetString(JsonElement element, string property)
  {
    if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
    {
      var s = value.GetString();
      return string.IsNullOrEmpty(s) ? null : s;
    }

    return null;
  }

  private static int GetInt(JsonElement element, string property)
  {
    if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i))
    {
      return i;
    }

    return 0;
  }

  private static double GetDouble(JsonElement element, string property)
  {
    if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d))
    {
      return d;
    }

    return 0d;
  }
}
