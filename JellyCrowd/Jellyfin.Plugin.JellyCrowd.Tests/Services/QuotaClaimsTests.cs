using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="QuotaClaims"/> — reducing a user's fulfilled requests to the ones that own
/// distinct files, so overlapping claims are not billed twice.
/// </summary>
public class QuotaClaimsTests
{
  private static RequestRecord Claim(int tmdbId, int? season = null, int? episode = null, string mediaType = "tv")
    => new() { TmdbId = tmdbId, MediaType = mediaType, Season = season, Episode = episode, Title = "T" };

  private static List<int?> Seasons(IReadOnlyList<RequestRecord> kept)
  {
    var seasons = new List<int?>();
    foreach (var record in kept)
    {
      seasons.Add(record.Season);
    }

    return seasons;
  }

  [Fact]
  public void Deduplicate_KeepsOnlyTheWholeSeries_WhenItsSeasonsAreAlsoClaimed()
  {
    // The exact shape that put a user over quota: "Stalk" claimed as a whole series AND as S01+S02+S03,
    // billing every episode of the show twice.
    var kept = QuotaClaims.Deduplicate(new[]
    {
      Claim(100604),
      Claim(100604, season: 1),
      Claim(100604, season: 2),
      Claim(100604, season: 3),
    });

    var record = Assert.Single(kept);
    Assert.Null(record.Season);
  }

  [Fact]
  public void Deduplicate_KeepsTheSeason_AndDropsItsEpisodes()
  {
    var kept = QuotaClaims.Deduplicate(new[]
    {
      Claim(7, season: 2, episode: 1),
      Claim(7, season: 2),
      Claim(7, season: 2, episode: 2),
    });

    var record = Assert.Single(kept);
    Assert.Equal(2, record.Season);
    Assert.Null(record.Episode);
  }

  [Fact]
  public void Deduplicate_KeepsIndependentClaims()
  {
    var kept = QuotaClaims.Deduplicate(new[]
    {
      Claim(7, season: 1),
      Claim(7, season: 2),
      Claim(8, season: 1),
      Claim(9, mediaType: "movie"),
    });

    Assert.Equal(4, kept.Count);
  }

  [Fact]
  public void Deduplicate_CollapsesExactDuplicates()
  {
    var kept = QuotaClaims.Deduplicate(new[] { Claim(7, season: 1), Claim(7, season: 1) });

    Assert.Single(kept);
  }

  [Fact]
  public void Deduplicate_DropsACoveredClaim_WhateverTheOrder()
  {
    // The broadest claim wins even when it arrives last: it is the one whose size covers the rest.
    Assert.Equal(new List<int?> { null }, Seasons(QuotaClaims.Deduplicate(new[] { Claim(7, season: 1), Claim(7) })));
    Assert.Equal(new List<int?> { null }, Seasons(QuotaClaims.Deduplicate(new[] { Claim(7), Claim(7, season: 1) })));
  }

  [Fact]
  public void Deduplicate_KeepsMoviesOfTheSameIdAcrossMediaTypesApart()
  {
    var kept = QuotaClaims.Deduplicate(new[] { Claim(7, mediaType: "movie"), Claim(7) });

    Assert.Equal(2, kept.Count);
  }

  [Fact]
  public void Deduplicate_HandlesAnEmptyList()
    => Assert.Empty(QuotaClaims.Deduplicate(System.Array.Empty<RequestRecord>()));
}
