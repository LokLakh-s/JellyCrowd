using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="TvKeywordGenres"/>: Horror and Thriller on the TV tab, backed by TMDB keywords.
/// </summary>
public class TvKeywordGenresTests
{
  private static readonly IReadOnlyList<Genre> TvGenres = new List<Genre>
  {
    new() { Id = 18, Name = "Drama" },
    new() { Id = 10765, Name = "Sci-Fi & Fantasy" },
    new() { Id = 37, Name = "Western" }
  };

  [Fact]
  public void Merge_AddsHorrorAndThriller_SortedAmongTheOthers()
  {
    var movie = new List<Genre> { new() { Id = 27, Name = "Horror" }, new() { Id = 53, Name = "Thriller" } };

    var merged = TvKeywordGenres.Merge(TvGenres, movie);

    Assert.Equal(new[] { "Drama", "Horror", "Sci-Fi & Fantasy", "Thriller", "Western" }, merged.Select(g => g.Name));
  }

  [Fact]
  public void Merge_UsesTheMovieLabel_SoTheNameIsLocalized()
  {
    var movie = new List<Genre> { new() { Id = 27, Name = "Horreur" } };

    var merged = TvKeywordGenres.Merge(TvGenres, movie);

    Assert.Equal("Horreur", merged.Single(g => g.Id == 27).Name);
    Assert.Equal("Thriller", merged.Single(g => g.Id == 53).Name); // missing label -> English fallback
  }

  [Fact]
  public void Merge_DoesNotDuplicate_WhenTmdbAlreadyListsTheGenreForShows()
  {
    var tv = new List<Genre> { new() { Id = 27, Name = "Horror" }, new() { Id = 99, Name = "Thriller" } };

    var merged = TvKeywordGenres.Merge(tv, Array.Empty<Genre>());

    Assert.Equal(2, merged.Count);
  }

  [Theory]
  [InlineData("27", null, "315058")]
  [InlineData("53", null, "316362")]
  [InlineData("27,18", "18", "315058")]
  [InlineData("27,53", null, "315058,316362")]
  [InlineData("18,10765", "18,10765", null)]
  [InlineData(" 27 , 18 ", "18", "315058")]
  public void Split_MovesKeywordGenresToKeywords(string input, string? genres, string? keywords)
  {
    var split = TvKeywordGenres.Split(input);

    Assert.Equal(genres, split.Genres);
    Assert.Equal(keywords, split.Keywords);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("  ")]
  public void Split_Empty_ReturnsNothing(string? input)
  {
    Assert.Equal((null, null), TvKeywordGenres.Split(input));
  }
}
