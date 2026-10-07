using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Models;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using Season = MediaBrowser.Controller.Entities.TV.Season;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="LibraryMatcher"/> with a TMDB show Sonarr files under another series: TMDB's
/// "Berlin and the Lady with an Ermine" (308014, one season) is season 2 of "Berlin (2023)" in the library,
/// whose own TMDB entry (146176, "Berlin") is season 1.
/// </summary>
public sealed class LibraryMatcherMappingTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonSeriesMappingStore _mappings;
  private readonly Series _berlin = new() { Id = Guid.NewGuid(), Name = "Berlin" };
  private readonly Season _season1 = new() { Id = Guid.NewGuid(), IndexNumber = 1 };
  private readonly Season _season2 = new() { Id = Guid.NewGuid(), IndexNumber = 2 };
  private readonly List<Episode> _episodes = new();
  private readonly List<BaseItem> _extraSeries = new();
  private readonly List<Episode> _handAdded = new();
  private readonly LibraryMatcher _matcher;

  public LibraryMatcherMappingTests()
  {
    _berlin.SetProviderId(MetadataProvider.Tmdb, "146176");
    _berlin.SetProviderId(MetadataProvider.Tvdb, "413033");
    for (var n = 1; n <= 8; n++)
    {
      _episodes.Add(new Episode { Id = Guid.NewGuid(), ParentIndexNumber = 1, IndexNumber = n, Size = 100 });
      _episodes.Add(new Episode { Id = Guid.NewGuid(), ParentIndexNumber = 2, IndexNumber = n, Size = 1000 });
    }

    var manager = new Mock<ILibraryManager>();
    manager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns<InternalItemsQuery>(Query);
    _mappings = new JsonSeriesMappingStore(_path);
    var mapping = new SeriesMapping { TmdbId = 308014, TvdbId = 413033 };
    mapping.Seasons.Add(new SeasonLink { TmdbSeason = 1, SonarrSeason = 2 });
    _mappings.SetAsync(mapping, CancellationToken.None).GetAwaiter().GetResult();
    _matcher = new LibraryMatcher(manager.Object, _mappings);
  }

  public void Dispose()
  {
    _mappings.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  // A tiny library: the one series, its two seasons and their episodes, filtered as Jellyfin would.
  private List<BaseItem> Query(InternalItemsQuery q)
  {
    IEnumerable<BaseItem> items = q.IncludeItemTypes.Contains(BaseItemKind.Series) ? new BaseItem[] { _berlin }.Concat(_extraSeries)
      : q.IncludeItemTypes.Contains(BaseItemKind.Season) ? new BaseItem[] { _season1, _season2 }
      : q.IncludeItemTypes.Contains(BaseItemKind.Episode) ? _episodes
      : Array.Empty<BaseItem>();
    if (q.HasAnyProviderId is { Count: > 0 } ids)
    {
      items = items.Where(i => ids.Any(p => i.GetProviderId(p.Key) == p.Value));
    }

    if (q.AncestorIds is { Length: > 0 } ancestors && q.IncludeItemTypes.Contains(BaseItemKind.Episode))
    {
      items = ancestors.Contains(_berlin.Id) ? items.Where(i => !_handAdded.Contains(i)) : _handAdded;
    }

    if (q.ParentIndexNumber is int season)
    {
      items = items.Where(i => i.ParentIndexNumber == season);
    }

    if (q.IndexNumber is int number)
    {
      items = items.Where(i => i.IndexNumber == number);
    }

    if (q.Limit is int limit)
    {
      items = items.Take(limit);
    }

    return items.ToList();
  }

  [Fact]
  public void TheMappedShow_IsFoundInItsSeriesSeason_InTmdbNumbering()
  {
    Assert.Equal(_episodes.Single(e => e.ParentIndexNumber == 2 && e.IndexNumber == 3).Id.ToString("N"), _matcher.FindEpisodeItemId(308014, 1, 3));
    Assert.Equal(_season2.Id.ToString("N"), _matcher.FindSeasonItemId(308014, 1));
    Assert.Equal(Enumerable.Range(1, 8).Select(n => new EpisodeKey(1, n)), _matcher.ListEpisodeKeys(308014, null).OrderBy(k => k.Episode));
    Assert.Empty(_matcher.ListEpisodeKeys(308014, 2));
  }

  [Fact]
  public void TheMappedShow_IsItsSeason_NotTheWholeSeries()
  {
    // Deleting "the show" must take its season, never the parent's season 1 with it.
    Assert.Equal(_season2.Id.ToString("N"), _matcher.FindItemId("tv", 308014));
    Assert.Equal(8000, _matcher.GetSizeBytes("tv", 308014));
  }

  [Fact]
  public void TheHostSeries_NoLongerCountsTheMappedSeasonAsItsOwn()
  {
    Assert.Equal(Enumerable.Range(1, 8).Select(n => new EpisodeKey(1, n)), _matcher.ListEpisodeKeys(146176, null).OrderBy(k => k.Episode));
    Assert.Null(_matcher.FindSeasonItemId(146176, 2));
    Assert.Equal(800, _matcher.GetSizeBytes("tv", 146176));
  }

  [Fact]
  public void ListLibraryMedia_ListsTheMappedSeasonAsTheMappedShows()
  {
    var media = _matcher.ListLibraryMedia();

    Assert.Contains(media, m => m.TmdbId == 146176 && m.Season == 1 && m.JellyfinItemId == _season1.Id.ToString("N"));
    Assert.Contains(media, m => m.TmdbId == 308014 && m.Season == 1 && m.JellyfinItemId == _season2.Id.ToString("N"));
  }

  [Fact]
  public void ACopyAddedByHandUnderTheShowsOwnTmdbId_StillCounts()
  {
    // The files dropped in a folder Jellyfin identified as TMDB 308014, outside Sonarr.
    var manual = new Series { Id = Guid.NewGuid(), Name = "Berlin et La Dame à l'hermine" };
    manual.SetProviderId(MetadataProvider.Tmdb, "308014");
    _extraSeries.Add(manual);
    var episode = new Episode { Id = Guid.NewGuid(), ParentIndexNumber = 1, IndexNumber = 1, Size = 5 };
    _handAdded.Add(episode);
    _episodes.Add(episode);

    Assert.Equal(manual.Id.ToString("N"), _matcher.FindItemId("tv", 308014));
    Assert.Equal(new[] { new EpisodeKey(1, 1) }, _matcher.ListEpisodeKeys(308014, 1));
  }
}
