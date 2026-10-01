using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for the per-title quality profile <see cref="ServarrDownloadClient"/> sends to Radarr/Sonarr.
/// </summary>
public class ServarrDownloadClientProfileTests
{
  private const string Radarr = "http://localhost:7878";
  private const string Sonarr = "http://localhost:8989";
  private static readonly Func<CancellationToken, Task> NoDelay = _ => Task.CompletedTask;

  private static PluginConfiguration Config() => new()
  {
    RadarrUrl = Radarr,
    RadarrApiKey = "rk",
    RadarrRootFolderPath = "/movies",
    RadarrQualityProfileId = 4,
    SonarrUrl = Sonarr,
    SonarrApiKey = "sk",
    SonarrRootFolderPath = "/tv",
    SonarrQualityProfileId = 5,
    SonarrLanguageProfileId = 1
  };

  private static IServarrProfileResolver Resolving(int? profile)
    => Mock.Of<IServarrProfileResolver>(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()) == Task.FromResult(profile));

  private static DownloadDispatch Movie() => new() { TmdbId = 603, MediaType = "movie", Title = "The Matrix" };

  [Fact]
  public async Task NewMovie_IsAddedWithTheResolvedProfile()
  {
    JsonObject? added = null;
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.LookupMovieAsync(Radarr, "rk", 603, It.IsAny<CancellationToken>())).ReturnsAsync(new JsonObject { ["tmdbId"] = 603 });
    servarr.Setup(s => s.AddMovieAsync(Radarr, "rk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
      .Callback<string, string, JsonObject, CancellationToken>((_, _, b, _) => added = b)
      .Returns(Task.CompletedTask);
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), Config, NoDelay, Resolving(7));

    await client.DispatchAsync(Movie(), CancellationToken.None);

    Assert.Equal(7, added!["qualityProfileId"]!.GetValue<int>());
  }

  [Fact]
  public async Task ExistingMovie_WithAnotherProfile_IsSwitchedThenSearched()
  {
    // A second requester with another preference: the title moves to the profile everyone is due.
    JsonObject? updated = null;
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetMovieByTmdbAsync(Radarr, "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["id"] = 9, ["qualityProfileId"] = 7 });
    servarr.Setup(s => s.UpdateMovieAsync(Radarr, "rk", 9, It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
      .Callback<string, string, int, JsonObject, CancellationToken>((_, _, _, b, _) => updated = b)
      .Returns(Task.CompletedTask);
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), Config, NoDelay, Resolving(4));

    await client.DispatchAsync(Movie(), CancellationToken.None);

    Assert.Equal(4, updated!["qualityProfileId"]!.GetValue<int>());
    servarr.Verify(s => s.CommandAsync(Radarr, "rk", It.Is<JsonObject>(c => c["name"]!.GetValue<string>() == "MoviesSearch"), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task ExistingMovie_WithTheSameProfile_IsNotUpdated()
  {
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetMovieByTmdbAsync(Radarr, "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["id"] = 9, ["qualityProfileId"] = 4 });
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), Config, NoDelay, Resolving(4));

    await client.DispatchAsync(Movie(), CancellationToken.None);

    servarr.Verify(s => s.UpdateMovieAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task ResolverFailure_FallsBackToTheConfiguredProfile()
  {
    JsonObject? added = null;
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.LookupMovieAsync(Radarr, "rk", 603, It.IsAny<CancellationToken>())).ReturnsAsync(new JsonObject { ["tmdbId"] = 603 });
    servarr.Setup(s => s.AddMovieAsync(Radarr, "rk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
      .Callback<string, string, JsonObject, CancellationToken>((_, _, b, _) => added = b)
      .Returns(Task.CompletedTask);
    var resolver = new Mock<IServarrProfileResolver>();
    resolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("store"));
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), Config, NoDelay, resolver.Object);

    await client.DispatchAsync(Movie(), CancellationToken.None);

    Assert.Equal(4, added!["qualityProfileId"]!.GetValue<int>());
  }

  [Fact]
  public async Task ExistingSeries_WithAnotherProfile_IsSwitched()
  {
    // One profile per show in Sonarr: a request for another season can move the whole show.
    var series = new JsonObject
    {
      ["id"] = 55,
      ["qualityProfileId"] = 8,
      ["monitored"] = true,
      ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 2, ["monitored"] = true })
    };
    var profiles = new System.Collections.Generic.List<int>();
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetSeriesByTvdbAsync(Sonarr, "sk", 81189, It.IsAny<CancellationToken>())).ReturnsAsync(series);
    servarr.Setup(s => s.GetEpisodesAsync(Sonarr, "sk", 55, It.IsAny<CancellationToken>()))
      .ReturnsAsync("[ { \"id\": 21, \"seasonNumber\": 2, \"episodeNumber\": 1, \"monitored\": true } ]");
    servarr.Setup(s => s.UpdateSeriesAsync(Sonarr, "sk", 55, It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
      .Callback<string, string, int, JsonObject, CancellationToken>((_, _, _, b, _) => profiles.Add(b["qualityProfileId"]!.GetValue<int>()))
      .Returns(Task.CompletedTask);
    var tmdb = Mock.Of<ITmdbClient>(t => t.GetTvdbIdAsync(1396, It.IsAny<CancellationToken>()) == Task.FromResult<int?>(81189));
    var client = new ServarrDownloadClient(servarr.Object, tmdb, Config, NoDelay, Resolving(5));

    await client.DispatchAsync(new DownloadDispatch { TmdbId = 1396, MediaType = "tv", Title = "Breaking Bad", Season = 2 }, CancellationToken.None);

    Assert.NotEmpty(profiles);
    Assert.All(profiles, p => Assert.Equal(5, p));
  }

  [Fact]
  public async Task NoSayFromPreferences_AnExistingTitleKeepsItsProfile()
  {
    // Feature off or nobody with a preference: a profile set by hand in Radarr must survive a re-dispatch.
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.GetMovieByTmdbAsync(Radarr, "rk", 603, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new JsonObject { ["id"] = 9, ["qualityProfileId"] = 12 });
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), Config, NoDelay, Resolving(null));

    await client.DispatchAsync(Movie(), CancellationToken.None);

    servarr.Verify(s => s.UpdateMovieAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task WithoutAResolver_TheConfiguredProfileIsUsed()
  {
    JsonObject? added = null;
    var servarr = new Mock<IServarrClient>();
    servarr.Setup(s => s.LookupMovieAsync(Radarr, "rk", 603, It.IsAny<CancellationToken>())).ReturnsAsync(new JsonObject { ["tmdbId"] = 603 });
    servarr.Setup(s => s.AddMovieAsync(Radarr, "rk", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
      .Callback<string, string, JsonObject, CancellationToken>((_, _, b, _) => added = b)
      .Returns(Task.CompletedTask);
    var client = new ServarrDownloadClient(servarr.Object, Mock.Of<ITmdbClient>(), Config);

    await client.DispatchAsync(Movie(), CancellationToken.None);

    Assert.Equal(4, added!["qualityProfileId"]!.GetValue<int>());
  }
}
