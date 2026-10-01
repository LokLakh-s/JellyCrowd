using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ContentRestrictionService"/>.
/// </summary>
public class ContentRestrictionServiceTests
{
  private static readonly Guid Kid = Guid.NewGuid();

  // A slice of Jellyfin's rating tables (scores are minimum ages), per country.
  private static readonly Dictionary<(string Country, string Rating), (int Score, int? Sub)> Scores = new()
  {
    [("US", "G")] = (0, 0),
    [("US", "PG")] = (10, 0),
    [("US", "PG-13")] = (13, 0),
    [("US", "R")] = (17, 0),
    [("US", "TV-14")] = (14, 0),
    [("US", "TV-MA")] = (17, 0),
    [("FR", "10")] = (10, null),
    [("FR", "12")] = (12, null),
    [("FR", "16")] = (16, null),
  };

  private static ContentRestrictionService Create(
    UserPolicy? policy,
    StubTmdbClient? tmdb = null,
    PluginConfiguration? config = null,
    string metadataCountry = "US")
  {
    var users = new Mock<IUserManager>();
    var user = new User("kid", "Prov", "Prov");
    users.Setup(m => m.GetUserById(Kid)).Returns(user);
    users.Setup(m => m.GetUserDto(user, It.IsAny<string>())).Returns(new UserDto { Policy = policy ?? new UserPolicy() });

    var localization = new Mock<ILocalizationManager>();
    localization.Setup(m => m.GetRatingScore(It.IsAny<string>(), It.IsAny<string?>()))
      .Returns((string rating, string? country) =>
        Scores.TryGetValue(((country ?? string.Empty).ToUpperInvariant(), rating), out var s)
          ? new ParentalRatingScore(s.Score, s.Sub)
          : null);

    var server = new Mock<IServerConfigurationManager>();
    server.Setup(m => m.Configuration).Returns(new ServerConfiguration { MetadataCountryCode = metadataCountry });

    var plugin = config ?? new PluginConfiguration();
    return new ContentRestrictionService(
      () => plugin,
      users.Object,
      localization.Object,
      server.Object,
      tmdb ?? new StubTmdbClient(),
      NullLogger<ContentRestrictionService>.Instance);
  }

  private static void Rate(StubTmdbClient tmdb, string mediaType, int tmdbId, string country, params string[] ratings)
    => tmdb.Certifications[mediaType + ":" + tmdbId] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
      [country] = ratings,
    };

  private static PluginConfiguration ChildGroup(int maxAge)
  {
    var config = new PluginConfiguration();
    config.ChildAccounts.Add(new ChildAccount { UserId = Kid, MaxAge = maxAge });
    return config;
  }

  [Fact]
  public void For_ReadsTheJellyfinParentalControl()
  {
    var service = Create(new UserPolicy { MaxParentalRating = 13, BlockUnratedItems = new[] { UnratedItem.Series } });

    var r = service.For(Kid);

    Assert.Equal(13, r.MaxScore);
    Assert.False(r.BlockUnratedMovies);
    Assert.True(r.BlockUnratedShows);
    Assert.False(r.IsChild);
  }

  [Fact]
  public void For_UnlimitedUser_IsUnrestricted()
  {
    Assert.False(Create(new UserPolicy()).For(Kid).IsRestricted);
  }

  [Fact]
  public void For_UnknownUser_IsUnrestricted()
  {
    Assert.False(Create(new UserPolicy { MaxParentalRating = 10 }).For(Guid.NewGuid()).IsRestricted);
  }

  [Fact]
  public void For_ChildGroupMember_IsAChildAccount()
  {
    var r = Create(new UserPolicy(), config: ChildGroup(12)).For(Kid);

    Assert.True(r.IsChild);
    Assert.Equal(12, r.MaxScore);
  }

  [Fact]
  public void For_AdministratorInAChildGroup_IsNotAChild()
  {
    var r = Create(new UserPolicy { IsAdministrator = true }, config: ChildGroup(12)).For(Kid);

    Assert.False(r.IsChild);
    Assert.False(r.IsRestricted);
  }

  [Theory]
  [InlineData("PG", true)]
  [InlineData("PG-13", true)]  // at the limit
  [InlineData("R", false)]
  public async Task IsAllowed_ScoresTheTitleWithJellyfinsRatings(string rating, bool expected)
  {
    var tmdb = new StubTmdbClient();
    Rate(tmdb, "movie", 1, "US", rating);
    var service = Create(new UserPolicy { MaxParentalRating = 13 }, tmdb);

    Assert.Equal(expected, await service.IsAllowedAsync(service.For(Kid), "movie", 1, CancellationToken.None));
  }

  [Fact]
  public async Task IsAllowed_UsesTheServerMetadataCountry()
  {
    // Rated 12 in France but R in the US: a French server judges it on the French rating.
    var tmdb = new StubTmdbClient();
    tmdb.Certifications["movie:1"] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
      ["FR"] = new[] { "12" },
      ["US"] = new[] { "R" },
    };
    var service = Create(new UserPolicy { MaxParentalRating = 12 }, tmdb, metadataCountry: "FR");

    Assert.True(await service.IsAllowedAsync(service.For(Kid), "movie", 1, CancellationToken.None));
  }

  [Fact]
  public async Task IsAllowed_SeveralReleases_JudgesOnTheStrictest()
  {
    var tmdb = new StubTmdbClient();
    Rate(tmdb, "movie", 1, "US", "PG-13", "R");
    var service = Create(new UserPolicy { MaxParentalRating = 13 }, tmdb);

    Assert.False(await service.IsAllowedAsync(service.For(Kid), "movie", 1, CancellationToken.None));
  }

  [Fact]
  public async Task IsAllowed_ShowsUseTheirContentRating()
  {
    var tmdb = new StubTmdbClient();
    Rate(tmdb, "tv", 7, "US", "TV-MA");
    var service = Create(new UserPolicy { MaxParentalRating = 14 }, tmdb);

    Assert.False(await service.IsAllowedAsync(service.For(Kid), "tv", 7, CancellationToken.None));
  }

  [Fact]
  public async Task IsAllowed_UnratedTitle_FollowsJellyfinsUnratedSetting()
  {
    var tmdb = new StubTmdbClient(); // no rating at all for the title
    var lenient = Create(new UserPolicy { MaxParentalRating = 10 }, tmdb);
    var strict = Create(new UserPolicy { MaxParentalRating = 10, BlockUnratedItems = new[] { UnratedItem.Movie } }, tmdb);

    Assert.True(await lenient.IsAllowedAsync(lenient.For(Kid), "movie", 1, CancellationToken.None));
    Assert.False(await strict.IsAllowedAsync(strict.For(Kid), "movie", 1, CancellationToken.None));
  }

  [Fact]
  public async Task IsAllowed_UnrecognisedRating_CountsAsUnrated()
  {
    var tmdb = new StubTmdbClient();
    Rate(tmdb, "movie", 1, "US", "NR");
    var service = Create(new UserPolicy(), tmdb, config: ChildGroup(16)); // a child never sees unrated

    Assert.False(await service.IsAllowedAsync(service.For(Kid), "movie", 1, CancellationToken.None));
  }

  [Fact]
  public async Task IsAllowed_Unrestricted_DoesNotLookTheRatingUp()
  {
    var tmdb = new StubTmdbClient { CertificationsUnavailable = true };
    var service = Create(new UserPolicy(), tmdb);

    Assert.True(await service.IsAllowedAsync(ContentRestriction.None, "movie", 1, CancellationToken.None));
  }

  [Fact]
  public async Task IsAllowed_RatingUnavailable_Throws()
  {
    var tmdb = new StubTmdbClient { CertificationsUnavailable = true };
    var service = Create(new UserPolicy { MaxParentalRating = 10 }, tmdb);

    await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(
      () => service.IsAllowedAsync(service.For(Kid), "movie", 1, CancellationToken.None));
  }

  [Fact]
  public async Task Filter_KeepsAllowedTitlesInOrder()
  {
    var tmdb = new StubTmdbClient();
    Rate(tmdb, "movie", 1, "US", "G");
    Rate(tmdb, "movie", 2, "US", "R");
    Rate(tmdb, "tv", 3, "US", "TV-14");
    Rate(tmdb, "movie", 4, "US", "PG");
    var service = Create(new UserPolicy { MaxParentalRating = 14 }, tmdb);
    var items = new List<CatalogItem>
    {
      new() { MediaType = "movie", TmdbId = 1 },
      new() { MediaType = "movie", TmdbId = 2 },
      new() { MediaType = "tv", TmdbId = 3 },
      new() { MediaType = "movie", TmdbId = 4 },
    };

    var kept = await service.FilterAsync(service.For(Kid), items, CancellationToken.None);

    Assert.Equal(new[] { 1, 3, 4 }, kept.Select(i => i.TmdbId));
  }

  [Fact]
  public async Task Filter_RatingUnavailable_HidesTheTitles()
  {
    var tmdb = new StubTmdbClient { CertificationsUnavailable = true };
    var service = Create(new UserPolicy { MaxParentalRating = 14 }, tmdb);

    var kept = await service.FilterAsync(service.For(Kid), new List<CatalogItem> { new() { MediaType = "movie", TmdbId = 1 } }, CancellationToken.None);

    Assert.Empty(kept);
  }

  [Fact]
  public async Task Filter_Unrestricted_ReturnsTheSameList()
  {
    var service = Create(new UserPolicy());
    var items = new List<CatalogItem> { new() { MediaType = "movie", TmdbId = 1 } };

    Assert.Same(items, await service.FilterAsync(ContentRestriction.None, items, CancellationToken.None));
  }
}
