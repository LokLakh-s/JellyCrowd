using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Jellyfin.Plugin.JellyCrowd.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="OrphanMediaCleaner"/>: what nobody owns any more leaves the library and Radarr/Sonarr,
/// in the chosen libraries only, once it has stayed so for the retention.
/// </summary>
public sealed class OrphanMediaCleanerTests : IDisposable
{
  private const string Films = "11111111111111111111111111111111";
  private const string Docs = "22222222222222222222222222222222";

  private static readonly Guid Dune = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
  private static readonly Guid ShowS1 = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb1");
  private static readonly Guid ShowS2 = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb2");
  private static readonly Guid Docu = Guid.Parse("cccccccccccccccccccccccccccccccc");

  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;
  private readonly Library _library = new();
  private readonly RecordingDownloadDispatcher _dispatcher = new();
  private readonly PluginConfiguration _config = new() { DeleteOrphanMedia = true, DeletionRetentionHours = 1 };
  private DateTime _now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

  public OrphanMediaCleanerTests()
  {
    _store = new JsonRequestStore(_path);
    _config.OrphanCleanupLibraryIds.Add(Films);
    _library.Add(Films, Dune, new LibraryMediaItem { TmdbId = 10, MediaType = "movie", Title = "Dune" });
    _library.Add(Films, ShowS1, new LibraryMediaItem { TmdbId = 20, MediaType = "tv", Season = 1, Title = "Show" });
    _library.Add(Films, ShowS2, new LibraryMediaItem { TmdbId = 20, MediaType = "tv", Season = 2, Title = "Show" });
    _library.Add(Docs, Docu, new LibraryMediaItem { TmdbId = 30, MediaType = "movie", Title = "Docu" });
  }

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  [Fact]
  public async Task Clean_WaitsTheRetention_ThenDeletesFromTheBackendAndTheLibrary()
  {
    await OwnAsync(20, "tv", null); // the whole show is owned: only Dune is an orphan
    var cleaner = Cleaner();

    Assert.Equal(0, await cleaner.CleanAsync(CancellationToken.None));
    _now = _now.AddMinutes(59);
    Assert.Equal(0, await cleaner.CleanAsync(CancellationToken.None));
    Assert.Empty(_library.Deleted);

    _now = _now.AddMinutes(1);
    Assert.Equal(1, await cleaner.CleanAsync(CancellationToken.None));

    Assert.Equal(new[] { Dune.ToString("N") }, _library.Deleted);
    var purge = Assert.Single(_dispatcher.Purged);
    Assert.Equal((10, "movie", (int?)null), (purge.Request.TmdbId, purge.Request.MediaType, purge.Request.Season));
    Assert.True(purge.LibraryDeletesFiles); // Jellyfin deletes the folder; the backend only drops the title
  }

  [Fact]
  public async Task Clean_IsOffByDefault()
  {
    var cleaner = Cleaner(new PluginConfiguration { DeletionRetentionHours = 0 });

    Assert.Equal(0, await cleaner.CleanAsync(CancellationToken.None));
    Assert.Empty(_library.Deleted);
    Assert.Empty(_dispatcher.Purged);
  }

  [Fact]
  public async Task Clean_WithNoLibraryChosen_DeletesNothing()
  {
    var config = new PluginConfiguration { DeleteOrphanMedia = true, DeletionRetentionHours = 0 };

    Assert.Equal(0, await Cleaner(config).CleanAsync(CancellationToken.None));
    Assert.Empty(_library.Deleted);
  }

  [Fact]
  public async Task Clean_LeavesTheLibrariesNotChosen()
  {
    _config.DeletionRetentionHours = 0;
    await OwnAsync(10, "movie", null);
    await OwnAsync(20, "tv", null);

    await Cleaner().CleanAsync(CancellationToken.None);

    Assert.Empty(_library.Deleted); // Docu is an orphan, but in Docs
  }

  [Fact]
  public async Task Clean_ASeasonOwnedOnItsOwn_ProtectsOnlyThatSeason()
  {
    _config.DeletionRetentionHours = 0;
    await OwnAsync(10, "movie", null);
    await OwnAsync(20, "tv", 1);

    await Cleaner().CleanAsync(CancellationToken.None);

    Assert.Equal(new[] { ShowS2.ToString("N") }, _library.Deleted);
    Assert.Equal(2, Assert.Single(_dispatcher.Purged).Request.Season);
  }

  [Fact]
  public async Task Clean_SparesWhatIsRequested_OrStillOwnedWhileItsDeletionIsPending()
  {
    _config.DeletionRetentionHours = 0;
    await OwnAsync(10, "movie", null, RequestStatus.Approved);      // on its way
    var flagged = await OwnAsync(20, "tv", 1);
    await _store.RequestDeletionAsync(flagged.Id, flagged.UserId, CancellationToken.None);
    await OwnAsync(20, "tv", 2, RequestStatus.Pending);

    await Cleaner().CleanAsync(CancellationToken.None);

    Assert.Empty(_library.Deleted);
  }

  [Fact]
  public async Task Clean_ADeniedRequest_DoesNotProtect()
  {
    _config.DeletionRetentionHours = 0;
    await OwnAsync(10, "movie", null, RequestStatus.Denied);
    await OwnAsync(20, "tv", null);

    await Cleaner().CleanAsync(CancellationToken.None);

    Assert.Equal(new[] { Dune.ToString("N") }, _library.Deleted);
  }

  [Fact]
  public async Task Clean_OwnedAgainBeforeTheRetention_StartsTheClockOver()
  {
    await OwnAsync(20, "tv", null);
    var cleaner = Cleaner();
    await cleaner.CleanAsync(CancellationToken.None);       // Dune: orphan since 12:00

    _now = _now.AddMinutes(30);
    var owner = await OwnAsync(10, "movie", null);
    await cleaner.CleanAsync(CancellationToken.None);       // owned again

    _now = _now.AddMinutes(40);
    await _store.DeleteAsync(owner.Id, CancellationToken.None);
    await cleaner.CleanAsync(CancellationToken.None);       // orphan again since 13:10

    _now = _now.AddMinutes(50);                              // 70 min after the first time, 50 after the second
    await cleaner.CleanAsync(CancellationToken.None);
    Assert.Empty(_library.Deleted);

    _now = _now.AddMinutes(10);
    await cleaner.CleanAsync(CancellationToken.None);
    Assert.Single(_library.Deleted);
  }

  [Fact]
  public async Task Clean_TurnedOffAndOnAgain_WaitsTheFullRetention()
  {
    await OwnAsync(20, "tv", null);
    var cleaner = Cleaner();
    await cleaner.CleanAsync(CancellationToken.None);

    _config.DeleteOrphanMedia = false;
    _now = _now.AddHours(2);
    await cleaner.CleanAsync(CancellationToken.None);
    _config.DeleteOrphanMedia = true;
    await cleaner.CleanAsync(CancellationToken.None);

    Assert.Empty(_library.Deleted);
  }

  [Fact]
  public async Task Clean_BackendUnreachable_RetriesBeforeDeleting_ThenGivesUpWaitingAfterAWeek()
  {
    _config.DeletionRetentionHours = 0;
    await OwnAsync(20, "tv", null);
    _dispatcher.PurgeSucceeds = false;
    var cleaner = Cleaner();

    Assert.Equal(0, await cleaner.CleanAsync(CancellationToken.None));
    Assert.Empty(_library.Deleted);

    _now = _now.AddDays(8);
    Assert.Equal(1, await cleaner.CleanAsync(CancellationToken.None));
    Assert.Single(_library.Deleted);
  }

  [Fact]
  public void IsWanted_FollowsTheScopeOfEachRequest()
  {
    var season2 = new LibraryMediaItem { TmdbId = 20, MediaType = "tv", Season = 2 };
    RequestRecord Req(int? season, int? episode, RequestStatus status = RequestStatus.Available)
      => new() { TmdbId = 20, MediaType = "tv", Season = season, Episode = episode, Status = status };

    Assert.True(OrphanMediaCleaner.IsWanted(season2, new[] { Req(null, null) }));
    Assert.True(OrphanMediaCleaner.IsWanted(season2, new[] { Req(2, 5) }));
    Assert.False(OrphanMediaCleaner.IsWanted(season2, new[] { Req(1, null) }));
    Assert.False(OrphanMediaCleaner.IsWanted(season2, new[] { Req(2, null, RequestStatus.Denied) }));
    Assert.False(OrphanMediaCleaner.IsWanted(season2, new[] { new RequestRecord { TmdbId = 20, MediaType = "movie", Status = RequestStatus.Available } }));
  }

  private OrphanMediaCleaner Cleaner(PluginConfiguration? config = null)
    => new(_store, _library, _library, _dispatcher, new NoOpActivityLog(), () => config ?? _config, NullLogger<OrphanMediaCleaner>.Instance, () => _now);

  private async Task<RequestRecord> OwnAsync(int tmdbId, string mediaType, int? season, RequestStatus status = RequestStatus.Available)
    => await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = tmdbId, MediaType = mediaType, Title = "T", Season = season, Status = status, AvailableAt = DateTime.UtcNow },
      CancellationToken.None);

  // A library of a few movies and seasons spread over two libraries, that records what it deletes.
  private sealed class Library : ILibraryMatcher, IMediaDeleter
  {
    private readonly List<(string LibraryId, Guid ItemId, LibraryMediaItem Item)> _items = new();

    public List<string> Deleted { get; } = new();

    public bool ScanRunning => false;

    public void Add(string libraryId, Guid itemId, LibraryMediaItem item)
    {
      item.JellyfinItemId = itemId.ToString("N");
      _items.Add((libraryId, itemId, item));
    }

    public IReadOnlySet<Guid> ItemsIn(IReadOnlyCollection<string> libraryIds)
      => _items.Where(i => libraryIds.Contains(i.LibraryId)).Select(i => i.ItemId).ToHashSet();

    public IReadOnlyList<LibraryMediaItem> ListLibraryMedia()
      => _items.Where(i => !Deleted.Contains(i.Item.JellyfinItemId)).Select(i => i.Item).ToList();

    public bool Exists(string jellyfinItemId) => _items.Any(i => i.Item.JellyfinItemId == jellyfinItemId) && !Deleted.Contains(jellyfinItemId);

    public bool Delete(string jellyfinItemId)
    {
      Deleted.Add(jellyfinItemId);
      return true;
    }

    public string? FindItemId(string mediaType, int tmdbId) => null;

    public bool Exists(string mediaType, int tmdbId) => false;

    public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode) => null;

    public string? FindSeasonItemId(int seriesTmdbId, int season) => null;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;

    public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode) => 0;

    public IReadOnlyCollection<EpisodeKey> ListEpisodeKeys(int seriesTmdbId, int? season) => Array.Empty<EpisodeKey>();
  }
}
