using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="TmdbResponseParser"/>.
/// </summary>
public class TmdbResponseParserTests
{
  private const string ListJson = """
  {
    "page": 1,
    "results": [
      { "id": 1, "media_type": "movie", "title": "Movie A", "overview": "o", "poster_path": "/p.jpg", "release_date": "2020-01-01", "vote_average": 7.5 },
      { "id": 2, "media_type": "tv", "name": "Show B", "first_air_date": "2019-05-05", "vote_average": 8.1 },
      { "id": 3, "media_type": "person", "name": "Someone" }
    ]
  }
  """;

  [Fact]
  public void ParseResults_SkipsPeople_AndMapsMovieAndTv()
  {
    var items = TmdbResponseParser.ParseResults(ListJson);

    Assert.Equal(2, items.Count);

    var movie = items[0];
    Assert.Equal(1, movie.TmdbId);
    Assert.Equal("movie", movie.MediaType);
    Assert.Equal("Movie A", movie.Title);
    Assert.Equal("2020-01-01", movie.ReleaseDate);
    Assert.Equal(7.5, movie.VoteAverage);
    Assert.False(movie.Available);

    var show = items[1];
    Assert.Equal(2, show.TmdbId);
    Assert.Equal("tv", show.MediaType);
    Assert.Equal("Show B", show.Title);
    Assert.Equal("2019-05-05", show.ReleaseDate);
  }

  [Fact]
  public void ParseResults_WithDefaultMediaType_AppliesWhenMissing()
  {
    const string json = """{ "results": [ { "id": 42, "title": "No Type", "release_date": "2022-02-02" } ] }""";

    var items = TmdbResponseParser.ParseResults(json, "movie");

    Assert.Single(items);
    Assert.Equal("movie", items[0].MediaType);
    Assert.Equal("No Type", items[0].Title);
  }

  [Fact]
  public void ParseResults_NoResultsArray_ReturnsEmpty()
  {
    Assert.Empty(TmdbResponseParser.ParseResults("""{ "status": "ok" }"""));
  }

  [Fact]
  public void ParseDetails_MapsSingleEntity()
  {
    const string json = """{ "id": 10, "title": "Detail Movie", "overview": "d", "release_date": "2021-02-02", "vote_average": 6.0 }""";

    var item = TmdbResponseParser.ParseDetails(json, "movie");

    Assert.NotNull(item);
    Assert.Equal(10, item!.TmdbId);
    Assert.Equal("Detail Movie", item.Title);
    Assert.Equal("2021-02-02", item.ReleaseDate);
  }

  [Fact]
  public void ParseDetails_Movie_MapsGenresRuntimeAndImdb()
  {
    const string json = """
    { "id": 10, "title": "M", "release_date": "2021-02-02", "vote_average": 6.0,
      "runtime": 131, "imdb_id": "tt1234567",
      "genres": [ { "id": 18, "name": "Drama" }, { "id": 878, "name": "Science Fiction" } ] }
    """;

    var item = TmdbResponseParser.ParseDetails(json, "movie");

    Assert.NotNull(item);
    Assert.Equal(131, item!.Runtime);
    Assert.Equal("tt1234567", item.ImdbId);
    Assert.Equal(new[] { "Drama", "Science Fiction" }, item.Genres);
  }

  [Fact]
  public void ParseDetails_Tv_UsesEpisodeRuntimeAndExternalImdb()
  {
    const string json = """
    { "id": 20, "name": "S", "first_air_date": "2019-01-01", "vote_average": 8.0,
      "episode_run_time": [ 50 ], "external_ids": { "imdb_id": "tt7654321" },
      "genres": [ { "id": 35, "name": "Comedy" } ] }
    """;

    var item = TmdbResponseParser.ParseDetails(json, "tv");

    Assert.NotNull(item);
    Assert.Equal(50, item!.Runtime);
    Assert.Equal("tt7654321", item.ImdbId);
    Assert.Equal(new[] { "Comedy" }, item.Genres);
  }

  [Fact]
  public void ParseDetails_Movie_ExtractsCollectionId()
  {
    const string json = """
    { "id": 10, "title": "M", "belongs_to_collection": { "id": 99, "name": "M Collection" } }
    """;

    var item = TmdbResponseParser.ParseDetails(json, "movie");

    Assert.Equal(99, item!.CollectionId);
  }

  [Fact]
  public void ParseDetails_NoCollection_NullCollectionId()
  {
    var item = TmdbResponseParser.ParseDetails("""{ "id": 10, "title": "M" }""", "movie");

    Assert.Null(item!.CollectionId);
  }

  [Fact]
  public void ParseDetails_MapsTopBilledCast_SkippingNamelessEntries()
  {
    const string json = """
    { "id": 10, "title": "M",
      "credits": { "cast": [
        { "name": "Alice A", "character": "Hero", "profile_path": "/a.jpg" },
        { "character": "Ghost" },
        { "name": "Bob B", "character": "Villain", "profile_path": null }
      ] } }
    """;

    var item = TmdbResponseParser.ParseDetails(json, "movie");

    Assert.NotNull(item);
    Assert.Equal(2, item!.Cast.Count);
    Assert.Equal("Alice A", item.Cast[0].Name);
    Assert.Equal("Hero", item.Cast[0].Character);
    Assert.Equal("/a.jpg", item.Cast[0].ProfilePath);
    Assert.Equal("Bob B", item.Cast[1].Name);
    Assert.Null(item.Cast[1].ProfilePath);
  }

  [Fact]
  public void ParseDetails_NoCredits_EmptyCast()
  {
    var item = TmdbResponseParser.ParseDetails("""{ "id": 10, "title": "M" }""", "movie");

    Assert.Empty(item!.Cast);
  }

  [Fact]
  public void ParseDetails_Movie_DirectorFromCrew_AndOriginalTitle()
  {
    const string json = """
    { "id": 10, "title": "Localized", "original_title": "Original",
      "credits": { "crew": [
        { "name": "Jane Doe", "job": "Director" },
        { "name": "Editor Guy", "job": "Editor" },
        { "name": "John Roe", "job": "Director" }
      ] } }
    """;

    var item = TmdbResponseParser.ParseDetails(json, "movie");

    Assert.Equal("Jane Doe, John Roe", item!.Director);
    Assert.Equal("Original", item.OriginalTitle);
  }

  [Fact]
  public void ParseDetails_OriginalTitle_NullWhenSameAsTitle()
  {
    var item = TmdbResponseParser.ParseDetails("""{ "id": 10, "title": "Same", "original_title": "Same" }""", "movie");

    Assert.Null(item!.OriginalTitle);
  }

  [Fact]
  public void ParseDetails_Tv_CreatorAsDirector()
  {
    const string json = """
    { "id": 20, "name": "Show", "created_by": [ { "name": "Vince Gilligan" } ] }
    """;

    var item = TmdbResponseParser.ParseDetails(json, "tv");

    Assert.Equal("Vince Gilligan", item!.Director);
  }

  [Fact]
  public void ParseCollectionParts_ReturnsMoviesInOrder()
  {
    const string json = """
    { "id": 99, "name": "Saga", "parts": [
      { "id": 1, "title": "Part 1", "release_date": "2000-01-01" },
      { "id": 2, "title": "Part 2", "release_date": "2003-01-01" }
    ] }
    """;

    var parts = TmdbResponseParser.ParseCollectionParts(json);

    Assert.Equal(2, parts.Count);
    Assert.Equal("movie", parts[0].MediaType);
    Assert.Equal(1, parts[0].TmdbId);
    Assert.Equal("Part 2", parts[1].Title);
  }

  [Fact]
  public void ParseSeasons_MapsNumberNameAndCount()
  {
    const string json = """
    { "seasons": [
      { "season_number": 1, "name": "Season 1", "episode_count": 10 },
      { "season_number": 2, "name": "Season 2", "episode_count": 8 }
    ] }
    """;

    var seasons = TmdbResponseParser.ParseSeasons(json);

    Assert.Equal(2, seasons.Count);
    Assert.Equal(1, seasons[0].SeasonNumber);
    Assert.Equal("Season 1", seasons[0].Name);
    Assert.Equal(10, seasons[0].EpisodeCount);
  }

  [Fact]
  public void ParseWatchProviders_MapsAndSortsByPriority()
  {
    const string json = """
    { "results": [
      { "provider_id": 337, "provider_name": "Disney+", "logo_path": "/d.jpg", "display_priority": 5 },
      { "provider_id": 8, "provider_name": "Netflix", "logo_path": "/n.jpg", "display_priority": 1 }
    ] }
    """;

    var providers = TmdbResponseParser.ParseWatchProviders(json);

    Assert.Equal(2, providers.Count);
    Assert.Equal("Netflix", providers[0].Name);
    Assert.Equal(8, providers[0].Id);
    Assert.Equal("/n.jpg", providers[0].LogoPath);
  }

  [Fact]
  public void ParseGenres_MapsIdAndName()
  {
    const string json = """{ "genres": [ { "id": 28, "name": "Action" }, { "id": 12, "name": "Adventure" } ] }""";

    var genres = TmdbResponseParser.ParseGenres(json);

    Assert.Equal(2, genres.Count);
    Assert.Equal(28, genres[0].Id);
    Assert.Equal("Action", genres[0].Name);
  }

  [Fact]
  public void ParseEpisodes_MapsNumberNameAndAirDate()
  {
    const string json = """
    { "episodes": [
      { "season_number": 2, "episode_number": 1, "name": "Pilot", "air_date": "2026-09-01", "still_path": "/s.jpg" },
      { "season_number": 2, "episode_number": 2, "name": "Next", "air_date": "2026-09-08" }
    ] }
    """;

    var episodes = TmdbResponseParser.ParseEpisodes(json);

    Assert.Equal(2, episodes.Count);
    Assert.Equal(2, episodes[0].SeasonNumber);
    Assert.Equal(1, episodes[0].EpisodeNumber);
    Assert.Equal("Pilot", episodes[0].Name);
    Assert.Equal("2026-09-01", episodes[0].AirDate);
    Assert.Equal("/s.jpg", episodes[0].StillPath);
    Assert.Null(episodes[1].StillPath);
  }

  [Fact]
  public void ParseEpisodes_NoEpisodes_ReturnsEmpty()
  {
    Assert.Empty(TmdbResponseParser.ParseEpisodes("{}"));
  }

  [Fact]
  public void ParseTvdbId_ReturnsId_WhenPresent()
  {
    Assert.Equal(81189, TmdbResponseParser.ParseTvdbId("""{ "id": 1396, "tvdb_id": 81189, "imdb_id": "tt0903747" }"""));
  }

  [Theory]
  [InlineData("""{ "id": 1396, "tvdb_id": 0 }""")]
  [InlineData("""{ "id": 1396, "tvdb_id": null }""")]
  [InlineData("""{ "id": 1396 }""")]
  public void ParseTvdbId_ReturnsNull_WhenMissingOrZero(string json)
  {
    Assert.Null(TmdbResponseParser.ParseTvdbId(json));
  }
}
