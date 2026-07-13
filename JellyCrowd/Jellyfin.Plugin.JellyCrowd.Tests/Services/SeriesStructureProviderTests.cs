using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="SeriesStructureProvider"/> — which season numbering the catalog offers.
/// </summary>
public class SeriesStructureProviderTests
{
  private const int TmdbId = 95479;   // Jujutsu Kaisen
  private const int TvdbId = 377543;

  private readonly Mock<ITmdbClient> _tmdb = new();
  private readonly Mock<IServarrClient> _servarr = new();
  private readonly PluginConfiguration _config = new()
  {
    DownloadBackend = "servarr",
    SonarrUrl = "http://sonarr:8989",
    SonarrApiKey = "key"
  };

  private SeriesStructureProvider Create()
    => new(_tmdb.Object, _servarr.Object, () => _config, NullLogger<SeriesStructureProvider>.Instance);

  private void TmdbSeasons(params Season[] seasons)
    => _tmdb.Setup(t => t.GetSeasonsAsync(TmdbId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seasons);

  private void TmdbEpisodes(int season, params Episode[] episodes)
    => _tmdb.Setup(t => t.GetSeasonEpisodesAsync(TmdbId, season, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(episodes);

  private void TvdbResolves()
    => _tmdb.Setup(t => t.GetTvdbIdAsync(TmdbId, It.IsAny<CancellationToken>())).ReturnsAsync(TvdbId);

  private void SonarrTracks(string json)
    => _servarr.Setup(s => s.GetSeriesByTvdbAsync(_config.SonarrUrl, _config.SonarrApiKey, TvdbId, It.IsAny<CancellationToken>()))
               .ReturnsAsync((JsonObject)JsonNode.Parse(json)!);

  private void SonarrDoesNotTrack(string? lookupJson)
  {
    _servarr.Setup(s => s.GetSeriesByTvdbAsync(_config.SonarrUrl, _config.SonarrApiKey, TvdbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JsonObject?)null);
    _servarr.Setup(s => s.LookupSeriesAsync(_config.SonarrUrl, _config.SonarrApiKey, TvdbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lookupJson is null ? null : (JsonObject)JsonNode.Parse(lookupJson)!);
  }

  // The reported bug: TMDB serves this anime as ONE season of 59 episodes, Sonarr (which downloads it)
  // splits it into 24 + 23 + 12. Offering TMDB's numbering makes seasons 2 and 3 unrequestable.
  [Fact]
  public async Task GetSeasonsAsync_SonarrBackend_OffersSonarrSeasons_NotTmdbsCollapsedOne()
  {
    TmdbSeasons(new Season { SeasonNumber = 1, Name = "Saison 1", EpisodeCount = 59 });
    TvdbResolves();
    SonarrTracks("""
    {
      "id": 12,
      "seasons": [
        { "seasonNumber": 1, "statistics": { "totalEpisodeCount": 24 } },
        { "seasonNumber": 2, "statistics": { "totalEpisodeCount": 23 } },
        { "seasonNumber": 3, "statistics": { "totalEpisodeCount": 12 } }
      ]
    }
    """);

    var seasons = await Create().GetSeasonsAsync(TmdbId, "fr-FR", CancellationToken.None);

    Assert.Equal(new[] { 1, 2, 3 }, seasons.Select(s => s.SeasonNumber));
    Assert.Equal(new int?[] { 24, 23, 12 }, seasons.Select(s => s.EpisodeCount));
    Assert.Equal("Saison 1", seasons[0].Name);   // TMDB's localized name where the numbering agrees
    Assert.Equal(string.Empty, seasons[1].Name); // TMDB knows no season 2 — the client labels it
  }

  [Fact]
  public async Task GetSeasonsAsync_NotTrackedYet_StillOffersSonarrNumbers_WithUnknownCounts()
  {
    TmdbSeasons(new Season { SeasonNumber = 1, Name = "Season 1", EpisodeCount = 59 });
    TvdbResolves();
    SonarrDoesNotTrack("""{ "id": 0, "seasons": [ { "seasonNumber": 1 }, { "seasonNumber": 2 } ] }""");

    var seasons = await Create().GetSeasonsAsync(TmdbId, "en-US", CancellationToken.None);

    Assert.Equal(new[] { 1, 2 }, seasons.Select(s => s.SeasonNumber));
    Assert.All(seasons, s => Assert.Null(s.EpisodeCount)); // unknown, so no count is shown — not "0"
  }

  [Fact]
  public async Task GetSeasonsAsync_NoSonarrBackend_UsesTmdb()
  {
    _config.DownloadBackend = "webhook";
    TmdbSeasons(new Season { SeasonNumber = 1, Name = "Season 1", EpisodeCount = 59 });

    var seasons = await Create().GetSeasonsAsync(TmdbId, "en-US", CancellationToken.None);

    Assert.Equal(59, Assert.Single(seasons).EpisodeCount);
    _servarr.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task GetSeasonsAsync_SonarrUnreachable_FallsBackToTmdb()
  {
    TmdbSeasons(new Season { SeasonNumber = 1, Name = "Season 1", EpisodeCount = 59 });
    TvdbResolves();
    _servarr.Setup(s => s.GetSeriesByTvdbAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("sonarr down"));

    var seasons = await Create().GetSeasonsAsync(TmdbId, "en-US", CancellationToken.None);

    Assert.Equal(59, Assert.Single(seasons).EpisodeCount);
  }

  // TMDB lists announced-but-empty seasons (Wednesday S3, 0 episodes). Offering them is pointless.
  [Fact]
  public async Task GetSeasonsAsync_DropsSeasonsKnownToHaveNoEpisodes()
  {
    _config.DownloadBackend = "webhook";
    TmdbSeasons(
      new Season { SeasonNumber = 1, Name = "Season 1", EpisodeCount = 8 },
      new Season { SeasonNumber = 2, Name = "Season 2", EpisodeCount = 8 },
      new Season { SeasonNumber = 3, Name = "Season 3", EpisodeCount = 0 });

    var seasons = await Create().GetSeasonsAsync(TmdbId, "en-US", CancellationToken.None);

    Assert.Equal(new[] { 1, 2 }, seasons.Select(s => s.SeasonNumber));
  }

  [Fact]
  public async Task GetEpisodesAsync_TrackedBySonarr_UsesSonarrEpisodes()
  {
    TvdbResolves();
    SonarrTracks("""{ "id": 12, "seasons": [ { "seasonNumber": 2 } ] }""");
    _servarr.Setup(s => s.GetEpisodesAsync(_config.SonarrUrl, _config.SonarrApiKey, 12, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""[ { "seasonNumber": 2, "episodeNumber": 1, "title": "Gojo's Past" } ]""");

    var episodes = await Create().GetEpisodesAsync(TmdbId, 2, "en-US", CancellationToken.None);

    var episode = Assert.Single(episodes);
    Assert.Equal(2, episode.SeasonNumber);
    Assert.Equal("Gojo's Past", episode.Name);
    _tmdb.Verify(t => t.GetSeasonEpisodesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  // Not tracked yet AND the two sources split the show differently: TMDB's episode numbers would be
  // dispatched to a Sonarr that cannot honour them, so offer none (the user takes the whole season).
  [Fact]
  public async Task GetEpisodesAsync_NotTracked_AndNumberingDiffers_OffersNoEpisodes()
  {
    TmdbSeasons(new Season { SeasonNumber = 1, Name = "Season 1", EpisodeCount = 59 });
    TmdbEpisodes(1, new Episode { SeasonNumber = 1, EpisodeNumber = 30, Name = "Should not be offered" });
    TvdbResolves();
    SonarrDoesNotTrack("""{ "id": 0, "seasons": [ { "seasonNumber": 1 }, { "seasonNumber": 2 } ] }""");

    var episodes = await Create().GetEpisodesAsync(TmdbId, 1, "en-US", CancellationToken.None);

    Assert.Empty(episodes);
  }

  [Fact]
  public async Task GetEpisodesAsync_NotTracked_ButNumberingAgrees_UsesTmdb()
  {
    TmdbSeasons(
      new Season { SeasonNumber = 1, Name = "Season 1", EpisodeCount = 10 },
      new Season { SeasonNumber = 2, Name = "Season 2", EpisodeCount = 10 });
    TmdbEpisodes(2, new Episode { SeasonNumber = 2, EpisodeNumber = 1, Name = "Pilot" });
    TvdbResolves();
    SonarrDoesNotTrack("""{ "id": 0, "seasons": [ { "seasonNumber": 1 }, { "seasonNumber": 2 } ] }""");

    var episodes = await Create().GetEpisodesAsync(TmdbId, 2, "en-US", CancellationToken.None);

    Assert.Equal("Pilot", Assert.Single(episodes).Name);
  }
}
