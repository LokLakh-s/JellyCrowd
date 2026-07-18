using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrEpisodeParser"/>.
/// </summary>
public class ServarrEpisodeParserTests
{
  private const string Json = """
  [
    { "id": 11, "seasonNumber": 1, "episodeNumber": 1, "episodeFileId": 101 },
    { "id": 12, "seasonNumber": 1, "episodeNumber": 2, "episodeFileId": 102 },
    { "id": 13, "seasonNumber": 1, "episodeNumber": 3, "episodeFileId": 0 },
    { "id": 21, "seasonNumber": 2, "episodeNumber": 1, "episodeFileId": 201 }
  ]
  """;

  [Fact]
  public void Select_WholeSeason_ReturnsEpisodesAndNonZeroFiles()
  {
    var (episodeIds, fileIds) = ServarrEpisodeParser.Select(Json, 1, null);

    Assert.Equal(new[] { 11, 12, 13 }, episodeIds);
    Assert.Equal(new[] { 101, 102 }, fileIds); // the file-less episode (0) is excluded
  }

  [Fact]
  public void Select_SingleEpisode_ReturnsOnlyThatEpisode()
  {
    var (episodeIds, fileIds) = ServarrEpisodeParser.Select(Json, 1, 2);

    Assert.Equal(new[] { 12 }, episodeIds);
    Assert.Equal(new[] { 102 }, fileIds);
  }

  [Fact]
  public void Select_OtherSeason_NotIncluded()
  {
    var (episodeIds, fileIds) = ServarrEpisodeParser.Select(Json, 2, null);

    Assert.Equal(new[] { 21 }, episodeIds);
    Assert.Equal(new[] { 201 }, fileIds);
    Assert.DoesNotContain(101, fileIds);
  }

  [Fact]
  public void Select_EmptyOrMissingSeason_ReturnsEmpty()
  {
    var (episodeIds, fileIds) = ServarrEpisodeParser.Select(Json, 9, null);

    Assert.Empty(episodeIds);
    Assert.Empty(fileIds);
    Assert.Empty(ServarrEpisodeParser.Select("[]", 1, null).EpisodeIds);
  }

  private const string TwoSeasons = """
  [
    { "id": 11, "seasonNumber": 0, "episodeNumber": 1 },
    { "id": 12, "seasonNumber": 1, "episodeNumber": 1 },
    { "id": 13, "seasonNumber": 1, "episodeNumber": 2 },
    { "id": 21, "seasonNumber": 2, "episodeNumber": 1 }
  ]
  """;

  [Fact]
  public void EpisodeIdsToMonitor_ForASeason_ReturnsThatSeasonsEpisodes()
  {
    Assert.Equal(new[] { 12, 13 }, ServarrEpisodeParser.EpisodeIdsToMonitor(TwoSeasons, 1));
    Assert.Equal(new[] { 21 }, ServarrEpisodeParser.EpisodeIdsToMonitor(TwoSeasons, 2));
  }

  [Fact]
  public void EpisodeIdsToMonitor_ForTheWholeSeries_ExcludesSpecials()
  {
    // A whole-series request monitors every real episode, but never season 0 (specials).
    Assert.Equal(new[] { 12, 13, 21 }, ServarrEpisodeParser.EpisodeIdsToMonitor(TwoSeasons, null));
  }
}
