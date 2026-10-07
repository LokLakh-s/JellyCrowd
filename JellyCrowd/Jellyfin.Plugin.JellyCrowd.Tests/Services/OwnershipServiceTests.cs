using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Jellyfin.Plugin.JellyCrowd.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="OwnershipService"/>: who owns what, giving and (silently) taking.
/// </summary>
public sealed class OwnershipServiceTests : IDisposable
{
  private static readonly Guid Alice = Guid.NewGuid();
  private static readonly Guid Bob = Guid.NewGuid();

  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;
  private readonly LibraryStub _library = new();
  private readonly RecordingNotificationService _notifications = new();
  private readonly RecordingActivityLog _activity = new();
  private readonly StubTmdbClient _tmdb = new();
  private readonly RecordingDownloadDispatcher _dispatcher = new();
  private readonly OwnershipService _service;

  public OwnershipServiceTests()
  {
    _store = new JsonRequestStore(_path);
    _service = new OwnershipService(_store, _library, new RequestCreationGate(), _notifications, _activity, _tmdb, _dispatcher, NullLogger<OwnershipService>.Instance, id => id == Alice ? "alice" : id == Bob ? "bob" : "other");
  }

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  // ---------- ListAsync ----------

  [Fact]
  public async Task ListAsync_ListsEveryLibraryMedia_WithItsOwners_OrphansIncluded()
  {
    _library.Movie(10, "Dune");
    _library.Movie(11, "Alien");
    await OwnAsync(Bob, "movie", 10, "Dune");
    await OwnAsync(Alice, "movie", 10, "Dune");

    var media = await _service.ListAsync(CancellationToken.None);

    Assert.Equal(new[] { "Alien", "Dune" }, media.Select(m => m.Title));
    Assert.Empty(media[0].Owners);
    Assert.Equal(new[] { "alice", "bob" }, media[1].Owners.Select(o => o.Name));
    Assert.Equal(1000, media[1].SizeBytes);
  }

  [Fact]
  public async Task ListAsync_SaysHowEachOwnerHoldsASeason()
  {
    _library.Season(20, "Show", 1);
    _library.Season(20, "Show", 2);
    await OwnAsync(Alice, "tv", 20, "Show"); // the whole show
    await OwnAsync(Bob, "tv", 20, "Show", season: 2, episode: 3);
    await OwnAsync(Bob, "tv", 20, "Show", season: 2, episode: 1);

    var media = await _service.ListAsync(CancellationToken.None);

    Assert.Equal(new int?[] { 1, 2 }, media.Select(m => m.Season));
    var alice = Assert.Single(media[0].Owners);
    Assert.True(alice.WholeShow);
    Assert.Empty(alice.Episodes);
    var bob = media[1].Owners.Single(o => o.UserId == Bob);
    Assert.False(bob.WholeShow);
    Assert.Equal(new[] { 1, 3 }, bob.Episodes);
  }

  [Fact]
  public async Task ListAsync_PrefersTheRequestsTitleAndPoster_AndFlagsAPendingDeletion()
  {
    _library.Season(30, "Berlin (2023)", 1);
    var owned = await OwnAsync(Alice, "tv", 30, "Berlin and the Lady with an Ermine", season: 1, poster: "/p.jpg");
    await _store.RequestDeletionAsync(owned.Id, Alice, CancellationToken.None);

    var media = Assert.Single(await _service.ListAsync(CancellationToken.None));

    Assert.Equal("Berlin and the Lady with an Ermine", media.Title);
    Assert.Equal("/p.jpg", media.PosterPath);
    Assert.True(Assert.Single(media.Owners).Leaving);
  }

  // ---------- GiveAsync ----------

  [Fact]
  public async Task GiveAsync_GivesEveryMediaToEveryMember_AndTellsThem()
  {
    _library.Movie(10, "Dune");
    _library.Season(20, "Show", 1);

    var result = await _service.GiveAsync(new[] { Alice, Bob }, new[] { Ref("movie", 10, "Dune"), Ref("tv", 20, "Show", 1) }, "admin", CancellationToken.None);

    Assert.Equal(4, result.Given);
    Assert.Equal(0, result.Failed);
    var all = await _store.GetAllAsync(CancellationToken.None);
    Assert.Equal(4, all.Count);
    Assert.All(all, r => Assert.Equal(RequestStatus.Available, r.Status));
    Assert.Contains(all, r => r.UserId == Bob && r.TmdbId == 20 && r.Season == 1 && r.JellyfinItemId == "season-20-1");
    Assert.Equal(4, _notifications.Personal.Count);
    Assert.Equal("admin gave Dune, Show S1 to alice, bob", Assert.Single(_activity.Messages));
  }

  [Fact]
  public async Task GiveAsync_AMediaNobodyRequested_TakesItsPosterFromTmdb()
  {
    _library.Movie(10, "Dune");
    _tmdb.Details = new CatalogItem { TmdbId = 10, MediaType = "movie", Title = "Dune", PosterPath = "/dune.jpg" };

    await _service.GiveAsync(new[] { Alice }, new[] { Ref("movie", 10, "Dune") }, "admin", CancellationToken.None);

    Assert.Equal("/dune.jpg", Assert.Single(await _store.GetAllAsync(CancellationToken.None)).PosterPath);
  }

  [Fact]
  public async Task GiveAsync_KeepsThePosterItIsGiven()
  {
    _library.Movie(10, "Dune");
    _tmdb.Details = new CatalogItem { TmdbId = 10, MediaType = "movie", Title = "Dune", PosterPath = "/other.jpg" };
    var media = Ref("movie", 10, "Dune");
    media.PosterPath = "/mine.jpg";

    await _service.GiveAsync(new[] { Alice }, new[] { media }, "admin", CancellationToken.None);

    Assert.Equal("/mine.jpg", Assert.Single(await _store.GetAllAsync(CancellationToken.None)).PosterPath);
  }

  [Fact]
  public async Task GiveAsync_AlreadyOwned_RenewsWithoutDuplicatingOrTelling()
  {
    _library.Movie(10, "Dune");
    var before = DateTime.UtcNow.AddDays(-50);
    await OwnAsync(Alice, "movie", 10, "Dune", availableAt: before);

    var result = await _service.GiveAsync(new[] { Alice }, new[] { Ref("movie", 10, "Dune") }, "admin", CancellationToken.None);

    Assert.Equal(1, result.AlreadyOwned);
    Assert.Equal(0, result.Given);
    var record = Assert.Single(await _store.GetAllAsync(CancellationToken.None));
    Assert.True(record.AvailableAt > before);
    Assert.Empty(_notifications.Personal);
    Assert.Empty(_activity.Messages);
  }

  [Fact]
  public async Task GiveAsync_NotInTheLibrary_FailsOncePerMedia_AndCreatesNothing()
  {
    var result = await _service.GiveAsync(new[] { Alice, Bob }, new[] { Ref("movie", 99, "Gone") }, "admin", CancellationToken.None);

    Assert.Equal(1, result.Failed);
    Assert.Empty(await _store.GetAllAsync(CancellationToken.None));
  }

  [Fact]
  public async Task GiveAsync_InvalidMedia_Fails()
  {
    var result = await _service.GiveAsync(new[] { Alice }, new[] { Ref("music", 10, "Dune"), Ref("movie", 10, " ") }, "admin", CancellationToken.None);

    Assert.Equal(2, result.Failed);
  }

  // ---------- RemoveAsync ----------

  [Fact]
  public async Task RemoveAsync_TakesTheMediaFromThoseMembersOnly_Silently()
  {
    _library.Movie(10, "Dune");
    await OwnAsync(Alice, "movie", 10, "Dune");
    await OwnAsync(Bob, "movie", 10, "Dune");

    var result = await _service.RemoveAsync(new[] { Alice }, new[] { Ref("movie", 10, "Dune") }, "admin", CancellationToken.None);

    Assert.Equal(1, result.Removed);
    Assert.Equal(Bob, Assert.Single(await _store.GetAllAsync(CancellationToken.None)).UserId);
    Assert.Empty(_notifications.Personal);
    Assert.Empty(_dispatcher.Cancelled); // a movie: cancelling would remove it from Radarr with its files
    Assert.Equal("admin took Dune from alice", Assert.Single(_activity.Messages));
  }

  [Fact]
  public async Task RemoveAsync_NotOwned_IsCounted_AndNothingIsLogged()
  {
    _library.Movie(10, "Dune");
    await OwnAsync(Bob, "movie", 10, "Dune");

    var result = await _service.RemoveAsync(new[] { Alice }, new[] { Ref("movie", 10, "Dune") }, "admin", CancellationToken.None);

    Assert.Equal(1, result.NotOwned);
    Assert.Equal(0, result.Removed);
    Assert.Single(await _store.GetAllAsync(CancellationToken.None));
    Assert.Empty(_activity.Messages);
  }

  [Fact]
  public async Task RemoveAsync_ASeason_TakesItsSeasonAndEpisodeOwnerships_ButNotTheOtherSeasons()
  {
    _library.Season(20, "Show", 1);
    _library.Season(20, "Show", 2);
    await OwnAsync(Alice, "tv", 20, "Show", season: 1);
    await OwnAsync(Alice, "tv", 20, "Show", season: 1, episode: 4);
    await OwnAsync(Alice, "tv", 20, "Show", season: 2);

    var result = await _service.RemoveAsync(new[] { Alice }, new[] { Ref("tv", 20, "Show", 1) }, "admin", CancellationToken.None);

    Assert.Equal(1, result.Removed);
    Assert.Equal(2, Assert.Single(await _store.GetAllAsync(CancellationToken.None)).Season);

    // Sonarr is told, for each ownership taken, after the store no longer holds it.
    Assert.Equal(new int?[] { 1, 1 }, _dispatcher.Cancelled.Select(r => r.Season));
  }

  [Fact]
  public async Task RemoveAsync_ASeasonOfAWholeShow_LeavesTheOtherSeasons_WithTheSameDates()
  {
    _library.Season(20, "Show", 1);
    _library.Season(20, "Show", 2);
    _library.Season(20, "Show", 3);
    var since = DateTime.UtcNow.AddDays(-30);
    var whole = await OwnAsync(Alice, "tv", 20, "Show", availableAt: since, poster: "/s.jpg");
    var requestedAt = whole.RequestedAt;
    var wholeId = whole.Id;

    var result = await _service.RemoveAsync(new[] { Alice }, new[] { Ref("tv", 20, "Show", 2) }, "admin", CancellationToken.None);

    Assert.Equal(1, result.Removed);
    var kept = (await _store.GetAllAsync(CancellationToken.None)).OrderBy(r => r.Season).ToList();
    Assert.Equal(new int?[] { 1, 3 }, kept.Select(r => r.Season));
    Assert.All(kept, r =>
    {
      Assert.Equal(RequestStatus.Available, r.Status);
      Assert.Equal(since, r.AvailableAt);
      Assert.Equal(requestedAt, r.RequestedAt);
      Assert.Equal("/s.jpg", r.PosterPath);
      Assert.NotEqual(wholeId, r.Id);
    });
    Assert.Equal("season-20-3", kept[1].JellyfinItemId);

    // The cut-up ownership does not count as new requests against the member's limit.
    Assert.Equal(0, await _store.CountUserRequestsSinceAsync(Alice, requestedAt.AddTicks(1), CancellationToken.None));
  }

  [Fact]
  public async Task RemoveAsync_SeveralSeasonsOfAWholeShow_LeavesOnlyTheRest()
  {
    _library.Season(20, "Show", 1);
    _library.Season(20, "Show", 2);
    _library.Season(20, "Show", 3);
    await OwnAsync(Alice, "tv", 20, "Show");

    var result = await _service.RemoveAsync(new[] { Alice }, new[] { Ref("tv", 20, "Show", 1), Ref("tv", 20, "Show", 2) }, "admin", CancellationToken.None);

    Assert.Equal(2, result.Removed);
    Assert.Equal(3, Assert.Single(await _store.GetAllAsync(CancellationToken.None)).Season);
  }

  [Fact]
  public async Task RemoveAsync_AWholeShowEntry_TakesEveryOwnershipOfTheShow()
  {
    _library.Show(20, "Show"); // a series with no season items: listed as one entry
    await OwnAsync(Alice, "tv", 20, "Show");
    await OwnAsync(Alice, "tv", 20, "Show", season: 1, episode: 2);

    var result = await _service.RemoveAsync(new[] { Alice }, new[] { Ref("tv", 20, "Show") }, "admin", CancellationToken.None);

    Assert.Equal(1, result.Removed);
    Assert.Empty(await _store.GetAllAsync(CancellationToken.None));
  }

  // ---------- OtherSeasons ----------

  [Fact]
  public void OtherSeasons_SkipsASeasonAlreadyOwnedOnItsOwn()
  {
    var whole = new RequestRecord { Id = Guid.NewGuid(), UserId = Alice, TmdbId = 20, MediaType = "tv", Title = "Show", Status = RequestStatus.Available };
    var library = new List<LibraryMediaItem>
    {
      new() { TmdbId = 20, MediaType = "tv", Season = 1, JellyfinItemId = "s1" },
      new() { TmdbId = 20, MediaType = "tv", Season = 2, JellyfinItemId = "s2" },
      new() { TmdbId = 20, MediaType = "tv", Season = 3, JellyfinItemId = "s3" },
      new() { TmdbId = 21, MediaType = "tv", Season = 4, JellyfinItemId = "other" }
    };
    var held = new List<RequestRecord>
    {
      whole,
      new() { Id = Guid.NewGuid(), UserId = Alice, TmdbId = 20, MediaType = "tv", Season = 3, Status = RequestStatus.Available }
    };

    var kept = OwnershipService.OtherSeasons(whole, 1, library, held);

    var season = Assert.Single(kept);
    Assert.Equal(2, season.Season);
    Assert.Equal("s2", season.JellyfinItemId);
  }

  private static OwnershipMediaRef Ref(string mediaType, int tmdbId, string title, int? season = null)
    => new() { MediaType = mediaType, TmdbId = tmdbId, Title = title, Season = season };

  private async Task<RequestRecord> OwnAsync(Guid userId, string mediaType, int tmdbId, string title, int? season = null, int? episode = null, DateTime? availableAt = null, string? poster = null)
    => await _store.CreateAsync(
      new RequestRecord
      {
        UserId = userId,
        TmdbId = tmdbId,
        MediaType = mediaType,
        Title = title,
        PosterPath = poster,
        Season = season,
        Episode = episode,
        Status = RequestStatus.Available,
        JellyfinItemId = "item",
        AvailableAt = availableAt ?? DateTime.UtcNow.AddDays(-1)
      },
      CancellationToken.None);

  private sealed class RecordingActivityLog : IActivityLog
  {
    public List<string> Messages { get; } = new();

    public Task LogAsync(string level, string category, string message, CancellationToken cancellationToken)
      => LogAsync(level, category, message, null, cancellationToken);

    public Task LogAsync(string level, string category, string message, string? user, CancellationToken cancellationToken)
    {
      Messages.Add(message);
      return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityEntry>> QueryAsync(string? term, string? category, string? level, string? user, int limit, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<ActivityEntry>>(new List<ActivityEntry>());
  }

  // A library of movies and show seasons; every listed season holds episodes.
  private sealed class LibraryStub : ILibraryMatcher
  {
    private readonly List<LibraryMediaItem> _media = new();

    public void Movie(int tmdbId, string title)
      => _media.Add(new LibraryMediaItem { JellyfinItemId = "movie-" + tmdbId, TmdbId = tmdbId, MediaType = "movie", Title = title, SizeBytes = 1000 });

    public void Season(int tmdbId, string title, int season)
      => _media.Add(new LibraryMediaItem { JellyfinItemId = "season-" + tmdbId + "-" + season, TmdbId = tmdbId, MediaType = "tv", Season = season, Title = title, SizeBytes = 500 });

    public void Show(int tmdbId, string title)
      => _media.Add(new LibraryMediaItem { JellyfinItemId = "series-" + tmdbId, TmdbId = tmdbId, MediaType = "tv", Title = title, SizeBytes = 500 });

    public bool Exists(string mediaType, int tmdbId) => FindItemId(mediaType, tmdbId) is not null;

    public string? FindItemId(string mediaType, int tmdbId)
      => _media.Any(m => m.TmdbId == tmdbId && m.MediaType == mediaType)
        ? (mediaType == "movie" ? "movie-" : "series-") + tmdbId
        : null;

    public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode)
      => _media.Any(m => m.TmdbId == seriesTmdbId && m.MediaType == "tv" && m.Season == season) ? "episode" : null;

    public string? FindSeasonItemId(int seriesTmdbId, int season)
      => _media.FirstOrDefault(m => m.TmdbId == seriesTmdbId && m.MediaType == "tv" && m.Season == season)?.JellyfinItemId;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;

    public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode) => 0;

    public IReadOnlyList<LibraryMediaItem> ListLibraryMedia() => _media;

    public IReadOnlyCollection<EpisodeKey> ListEpisodeKeys(int seriesTmdbId, int? season) => Array.Empty<EpisodeKey>();
  }
}
