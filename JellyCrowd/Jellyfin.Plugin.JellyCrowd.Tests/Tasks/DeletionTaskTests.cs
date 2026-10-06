using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Jellyfin.Plugin.JellyCrowd.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Tasks;

/// <summary>
/// Tests for <see cref="DeletionTask"/>.
/// </summary>
public sealed class DeletionTaskTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;

  public DeletionTaskTests()
  {
    _store = new JsonRequestStore(_path);
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
  public async Task Execute_DeletesDueMedia_AndRemovesRequest()
  {
    var id = await SeedFlaggedAsync("item-abc");
    var deleter = new RecordingDeleter();
    var dispatcher = new RecordingDispatcher();
    var promoter = new RecordingPromoter();
    var task = new DeletionTask(_store, deleter, dispatcher, new StubMatcher(), new RecordingNotificationService(), promoter, new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Contains("item-abc", deleter.Deleted);
    // Unowned deletion must also purge the download backend (Radarr/Sonarr) so a re-request is clean.
    Assert.Contains(id, dispatcher.Purged);
    Assert.Null(await _store.GetByIdAsync(id, CancellationToken.None));
    // Freeing space must trigger a quota-hold re-evaluation so held requests can resume.
    Assert.Equal(1, promoter.Calls);
  }

  [Fact]
  public async Task Execute_ItemInTheLibrary_LeavesItsFilesToJellyfin()
  {
    // Radarr deleting the folder while Jellyfin deleted it too made Radarr's own deletion fail half-way.
    await SeedFlaggedAsync("item-abc");
    var deleter = new RecordingDeleter();
    var dispatcher = new RecordingDispatcher();
    var task = new DeletionTask(_store, deleter, dispatcher, new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Equal(new[] { true }, dispatcher.LibraryDeletes);
    Assert.Contains("item-abc", deleter.Deleted);
  }

  [Fact]
  public async Task Execute_ItemGoneFromTheLibrary_LetsTheBackendDeleteTheFiles()
  {
    // Nothing in Jellyfin would delete them: the backend must, or they stay on disk.
    await SeedFlaggedAsync("item-abc");
    var deleter = new RecordingDeleter();
    deleter.Missing.Add("item-abc");
    var dispatcher = new RecordingDispatcher();
    var task = new DeletionTask(_store, deleter, dispatcher, new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Equal(new[] { false }, dispatcher.LibraryDeletes);
    Assert.Empty(deleter.Deleted);
  }

  [Fact]
  public async Task Execute_StaleStoredItemId_DeletesTheLiveItem()
  {
    // A library rescan / metadata refresh regenerates item ids, so the id recorded when the media landed
    // can dangle. Trusting it blindly deleted nothing at all — resolve the title live instead.
    var id = await SeedFlaggedAsync("stale-id-from-before-the-rescan");
    var deleter = new RecordingDeleter();
    var task = new DeletionTask(_store, deleter, new RecordingDispatcher(), new StubMatcher(itemId: "live-id"), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Contains("live-id", deleter.Deleted);
    Assert.DoesNotContain("stale-id-from-before-the-rescan", deleter.Deleted);
    Assert.Null(await _store.GetByIdAsync(id, CancellationToken.None));
  }

  [Fact]
  public async Task Execute_MatcherCannotIdentifyTheTitle_FallsBackToTheStoredItemId()
  {
    // The matcher can lose a title (its provider id was stripped) while the item is still there under the
    // id we recorded — so the stored id stays the fallback rather than deleting nothing.
    await SeedFlaggedAsync("item-abc");
    var deleter = new RecordingDeleter();
    var task = new DeletionTask(_store, deleter, new RecordingDispatcher(), new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Contains("item-abc", deleter.Deleted);
  }

  [Fact]
  public async Task Execute_KeepsMedia_WhenRetentionNotElapsed()
  {
    var id = await SeedFlaggedAsync("item-xyz");
    var deleter = new RecordingDeleter();
    var task = new DeletionTask(_store, deleter, new RecordingDispatcher(), new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 1_000_000 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Empty(deleter.Deleted);
    Assert.NotNull(await _store.GetByIdAsync(id, CancellationToken.None));
  }

  [Fact]
  public async Task Execute_KeepsRequest_WhenBackendPurgeFails()
  {
    // N18: a failed backend purge (e.g. backend down) must not finalize the deletion — keep the
    // request flagged and the media in place so it retries next run.
    var id = await SeedFlaggedAsync("item-fail");
    var deleter = new RecordingDeleter();
    var dispatcher = new RecordingDispatcher(purgeSucceeds: false);
    var task = new DeletionTask(_store, deleter, dispatcher, new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Contains(id, dispatcher.Purged);       // purge was attempted
    Assert.Empty(deleter.Deleted);                // media left in place
    Assert.NotNull(await _store.GetByIdAsync(id, CancellationToken.None)); // request kept for retry
  }

  [Fact]
  public async Task Execute_SharedMedia_RemovesOwnRequestButKeepsMediaAndBackend()
  {
    // Title still wanted by another active request → don't delete the file or purge the backend; just
    // drop this user's ownership row.
    var owner = Guid.NewGuid();
    var flagged = await _store.CreateAsync(new RequestRecord { UserId = owner, TmdbId = 42, MediaType = "movie", Title = "Shared" }, CancellationToken.None);
    await _store.MarkAvailableAsync(flagged.Id, "item-shared", CancellationToken.None);
    await _store.RequestDeletionAsync(flagged.Id, owner, CancellationToken.None);
    var other = await _store.CreateAsync(new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 42, MediaType = "movie", Title = "Shared" }, CancellationToken.None);
    await _store.MarkAvailableAsync(other.Id, "item-shared", CancellationToken.None);

    var deleter = new RecordingDeleter();
    var dispatcher = new RecordingDispatcher();
    var task = new DeletionTask(_store, deleter, dispatcher, new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Empty(deleter.Deleted);   // media kept (still owned by another user)
    Assert.Empty(dispatcher.Purged); // backend not purged
    Assert.Null(await _store.GetByIdAsync(flagged.Id, CancellationToken.None)); // this ownership removed
    Assert.NotNull(await _store.GetByIdAsync(other.Id, CancellationToken.None)); // other owner intact
  }

  [Fact]
  public async Task Execute_Season_DeletesTheSeasonItem_NotASingleEpisode()
  {
    var user = Guid.NewGuid();
    var created = await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 271347, MediaType = "tv", Title = "Shtisel", Season = 1 }, CancellationToken.None);
    await _store.MarkAvailableAsync(created.Id, "episode-item", CancellationToken.None); // reconciler stores a single EPISODE id
    await _store.RequestDeletionAsync(created.Id, user, CancellationToken.None);

    var deleter = new RecordingDeleter();
    var task = new DeletionTask(_store, deleter, new RecordingDispatcher(), new StubMatcher(seasonItemId: "season-item"), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Contains("season-item", deleter.Deleted);        // deletes the resolved Season (whole folder)
    Assert.DoesNotContain("episode-item", deleter.Deleted); // not the single stored episode
    Assert.Null(await _store.GetByIdAsync(created.Id, CancellationToken.None));
  }

  [Fact]
  public async Task Execute_Season_WhenSeasonUnresolved_DeletesNothing_NotASingleEpisode()
  {
    var user = Guid.NewGuid();
    var created = await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 1, MediaType = "tv", Title = "S", Season = 2 }, CancellationToken.None);
    await _store.MarkAvailableAsync(created.Id, "episode-item", CancellationToken.None);
    await _store.RequestDeletionAsync(created.Id, user, CancellationToken.None);

    var deleter = new RecordingDeleter();
    // StubMatcher() → FindSeasonItemId returns null: the season can't be resolved.
    var task = new DeletionTask(_store, deleter, new RecordingDispatcher(), new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Empty(deleter.Deleted); // must not fall back to deleting a single episode of the season
  }

  [Fact]
  public async Task Execute_Season_NotBlocked_ByAnotherSeasonStillOwned()
  {
    var userA = Guid.NewGuid();
    var userB = Guid.NewGuid();
    var s1 = await _store.CreateAsync(new RequestRecord { UserId = userA, TmdbId = 271347, MediaType = "tv", Title = "Show", Season = 1 }, CancellationToken.None);
    await _store.MarkAvailableAsync(s1.Id, "s1-episode", CancellationToken.None);
    await _store.RequestDeletionAsync(s1.Id, userA, CancellationToken.None);

    // Season 2 is still owned by another user — this must NOT block season 1's deletion (the reported bug).
    var s2 = await _store.CreateAsync(new RequestRecord { UserId = userB, TmdbId = 271347, MediaType = "tv", Title = "Show", Season = 2 }, CancellationToken.None);
    await _store.MarkAvailableAsync(s2.Id, "s2-episode", CancellationToken.None);

    var deleter = new RecordingDeleter();
    var task = new DeletionTask(_store, deleter, new RecordingDispatcher(), new StubMatcher(seasonItemId: "season-item"), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Contains("season-item", deleter.Deleted);                          // season 1 deleted…
    Assert.Null(await _store.GetByIdAsync(s1.Id, CancellationToken.None));    // …and its request cleared
    Assert.NotNull(await _store.GetByIdAsync(s2.Id, CancellationToken.None)); // season 2 left untouched
  }

  private async Task<RequestRecord> SeedOwnedAsync(Guid user, int? season, int? episode, bool flagForDeletion)
  {
    var created = await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 288385, MediaType = "tv", Title = "Paolo", Season = season, Episode = episode }, CancellationToken.None);
    await _store.MarkAvailableAsync(created.Id, "stored-item", CancellationToken.None);
    if (flagForDeletion)
    {
      await _store.RequestDeletionAsync(created.Id, user, CancellationToken.None);
    }

    return created;
  }

  private static StubMatcher SeasonOneInLibrary()
  {
    var matcher = new StubMatcher(seasonItemId: "season-item");
    matcher.Episodes.Add(new(1, 1));
    matcher.Episodes.Add(new(1, 2));
    matcher.Episodes.Add(new(1, 3));
    return matcher;
  }

  [Fact]
  public async Task Execute_Season_PartlyOwnedByAnotherUser_DeletesEveryOtherEpisode()
  {
    // A deletes season 1 while B owns its episode 2: everything but episode 2 goes, never the season folder.
    var userA = Guid.NewGuid();
    var season = await SeedOwnedAsync(userA, 1, null, flagForDeletion: true);
    var episode = await SeedOwnedAsync(Guid.NewGuid(), 1, 2, flagForDeletion: false);
    var deleter = new RecordingDeleter();
    var dispatcher = new RecordingDispatcher();
    var task = new DeletionTask(_store, deleter, dispatcher, SeasonOneInLibrary(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Equal(new[] { "ep-S1E1", "ep-S1E3" }, deleter.Deleted.OrderBy(d => d, StringComparer.Ordinal));
    Assert.Equal(new[] { season.Id }, dispatcher.Purged); // the backend withdraws the rest, B's episode excepted
    Assert.Null(await _store.GetByIdAsync(season.Id, CancellationToken.None));
    Assert.NotNull(await _store.GetByIdAsync(episode.Id, CancellationToken.None));
  }

  [Fact]
  public async Task Execute_Episode_InsideAnotherUsersSeason_DeletesNothing()
  {
    var episode = await SeedOwnedAsync(Guid.NewGuid(), 1, 2, flagForDeletion: false);
    await _store.RequestDeletionAsync(episode.Id, (await _store.GetByIdAsync(episode.Id, CancellationToken.None))!.UserId, CancellationToken.None);
    await SeedOwnedAsync(Guid.NewGuid(), 1, null, flagForDeletion: false);
    var deleter = new RecordingDeleter();
    var dispatcher = new RecordingDispatcher();
    var task = new DeletionTask(_store, deleter, dispatcher, SeasonOneInLibrary(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Empty(deleter.Deleted);
    Assert.Empty(dispatcher.Purged);
    Assert.Null(await _store.GetByIdAsync(episode.Id, CancellationToken.None)); // only the ownership goes
  }

  [Fact]
  public async Task Execute_PartialDeletion_BackendUnreachable_StaysFlaggedForTheNextRun()
  {
    var season = await SeedOwnedAsync(Guid.NewGuid(), 1, null, flagForDeletion: true);
    await SeedOwnedAsync(Guid.NewGuid(), 1, 2, flagForDeletion: false);
    var deleter = new RecordingDeleter();
    var task = new DeletionTask(_store, deleter, new RecordingDispatcher(purgeSucceeds: false), SeasonOneInLibrary(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => new PluginConfiguration { DeletionRetentionHours = 0 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Empty(deleter.Deleted);
    Assert.NotNull(await _store.GetByIdAsync(season.Id, CancellationToken.None));
  }

  [Fact]
  public async Task Execute_SweepsEmptySeries_AndProtectsActiveRequests_WhenEnabled()
  {
    // An active (Approved) request must be handed to the cleaner as "wanted" so its series is spared.
    var wantedReq = await _store.CreateAsync(new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 555, MediaType = "tv", Title = "Wanted" }, CancellationToken.None);
    await _store.UpdateStatusAsync(wantedReq.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);

    var cleaner = new RecordingCleaner();
    var task = new DeletionTask(_store, new RecordingDeleter(), new RecordingDispatcher(), new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), cleaner, () => new PluginConfiguration { RemoveEmptySeries = true, EmptySeriesMinAgeHours = 24 }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Equal(1, cleaner.Calls);
    Assert.Equal(24, cleaner.LastMinAgeHours);
    Assert.Contains(555, cleaner.LastWanted!);
  }

  [Fact]
  public async Task Execute_SkipsEmptySeriesSweep_WhenDisabled()
  {
    var cleaner = new RecordingCleaner();
    var task = new DeletionTask(_store, new RecordingDeleter(), new RecordingDispatcher(), new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), cleaner, () => new PluginConfiguration { RemoveEmptySeries = false }, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Equal(0, cleaner.Calls);
  }

  private async Task<Guid> SeedFlaggedAsync(string itemId)
  {
    var user = Guid.NewGuid();
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = 1, MediaType = "movie", Title = "X" },
      CancellationToken.None);
    await _store.MarkAvailableAsync(created.Id, itemId, CancellationToken.None);
    await _store.RequestDeletionAsync(created.Id, user, CancellationToken.None);
    return created.Id;
  }

  private sealed class StubMatcher : ILibraryMatcher
  {
    private readonly string? _seasonItemId;
    private readonly string? _itemId;

    public StubMatcher(string? seasonItemId = null, string? itemId = null)
    {
      _seasonItemId = seasonItemId;
      _itemId = itemId;
    }

    public bool Exists(string mediaType, int tmdbId) => false;

    public string? FindItemId(string mediaType, int tmdbId) => _itemId;

    /// <summary>Gets the episodes in the library (empty unless a test fills it).</summary>
    public System.Collections.Generic.HashSet<Jellyfin.Plugin.JellyCrowd.Models.EpisodeKey> Episodes { get; } = new();

    public string? FindEpisodeItemId(int seriesTmdbId, int? season, int? episode)
      => season is int s && episode is int e && Episodes.Contains(new Jellyfin.Plugin.JellyCrowd.Models.EpisodeKey(s, e))
        ? "ep-S" + s.ToString(System.Globalization.CultureInfo.InvariantCulture) + "E" + e.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : null;

    public string? FindSeasonItemId(int seriesTmdbId, int season) => _seasonItemId;

    public long GetSizeBytes(string mediaType, int tmdbId) => 0;

    public long GetSizeBytes(string mediaType, int tmdbId, int? season, int? episode) => 0;

    public System.Collections.Generic.IReadOnlyList<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem> ListLibraryMedia() => System.Array.Empty<Jellyfin.Plugin.JellyCrowd.Models.LibraryMediaItem>();

    public System.Collections.Generic.IReadOnlyCollection<Jellyfin.Plugin.JellyCrowd.Models.EpisodeKey> ListEpisodeKeys(int seriesTmdbId, int? season)
      => Episodes.Where(k => season is null || k.Season == season).ToList();
  }

  private sealed class RecordingPromoter : IQuotaHoldPromoter
  {
    public int Calls { get; private set; }

    public Task<int> PromoteAsync(CancellationToken cancellationToken)
    {
      Calls++;
      return Task.FromResult(0);
    }
  }

  [Fact]
  public async Task Expiry_ChildrenHaveAWindowOfTheirOwn()
  {
    var child = Guid.NewGuid();
    var adult = Guid.NewGuid();
    var twentyDaysAgo = DateTime.UtcNow.AddDays(-20);
    await _store.CreateAsync(new RequestRecord { UserId = child, TmdbId = 1, MediaType = "movie", Title = "Kid film", Status = RequestStatus.Available, AvailableAt = twentyDaysAgo }, CancellationToken.None);
    await _store.CreateAsync(new RequestRecord { UserId = adult, TmdbId = 2, MediaType = "movie", Title = "Grown-up film", Status = RequestStatus.Available, AvailableAt = twentyDaysAgo }, CancellationToken.None);
    var config = new PluginConfiguration { DeletionRetentionHours = 0, MediaExpiryDays = 90, ChildMediaExpiryDays = 10 };
    config.ChildAccounts.Add(new ChildAccount { UserId = child });
    var task = new DeletionTask(_store, new RecordingDeleter(), new RecordingDispatcher(), new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => config, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    var left = await _store.GetAllAsync(CancellationToken.None);
    Assert.DoesNotContain(left, r => r.UserId == child); // 20 days > the 10-day child window
    Assert.Contains(left, r => r.UserId == adult);       // 20 days < the 90-day general window
  }

  [Fact]
  public async Task Expiry_ChildWindowOfZero_NeverLapses()
  {
    var child = Guid.NewGuid();
    await _store.CreateAsync(new RequestRecord { UserId = child, TmdbId = 1, MediaType = "movie", Title = "Kid film", Status = RequestStatus.Available, AvailableAt = DateTime.UtcNow.AddDays(-400) }, CancellationToken.None);
    var config = new PluginConfiguration { DeletionRetentionHours = 0, MediaExpiryDays = 90, ChildMediaExpiryDays = 0 };
    config.ChildAccounts.Add(new ChildAccount { UserId = child });
    var task = new DeletionTask(_store, new RecordingDeleter(), new RecordingDispatcher(), new StubMatcher(), new RecordingNotificationService(), new RecordingPromoter(), new RecordingCleaner(), () => config, NullLogger<DeletionTask>.Instance);

    await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

    Assert.Single(await _store.GetAllAsync(CancellationToken.None));
  }

  private sealed class RecordingDeleter : IMediaDeleter
  {
    public List<string> Deleted { get; } = new();

    /// <summary>Gets the ids the library no longer has.</summary>
    public HashSet<string> Missing { get; } = new();

    public bool Exists(string jellyfinItemId) => !Missing.Contains(jellyfinItemId);

    public bool Delete(string jellyfinItemId)
    {
      Deleted.Add(jellyfinItemId);
      return true;
    }
  }

  private sealed class RecordingCleaner : IEmptyLibraryCleaner
  {
    public int Calls { get; private set; }

    public int LastMinAgeHours { get; private set; } = -1;

    public IReadOnlySet<int>? LastWanted { get; private set; }

    public int RemoveEmptySeries(int minAgeHours, IReadOnlySet<int> wantedTmdbIds)
    {
      Calls++;
      LastMinAgeHours = minAgeHours;
      LastWanted = wantedTmdbIds;
      return 0;
    }
  }

  private sealed class RecordingDispatcher : IDownloadDispatcher
  {
    private readonly bool _purgeSucceeds;

    public RecordingDispatcher(bool purgeSucceeds = true) => _purgeSucceeds = purgeSucceeds;

    public List<Guid> Purged { get; } = new();

    /// <summary>Gets, per purge, whether Jellyfin was to delete the files itself.</summary>
    public List<bool> LibraryDeletes { get; } = new();

    public Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task DispatchDueAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TestActiveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> PurgeAsync(RequestRecord request, bool libraryDeletesFiles, CancellationToken cancellationToken)
    {
      Purged.Add(request.Id);
      LibraryDeletes.Add(libraryDeletesFiles);
      return Task.FromResult(_purgeSucceeds);
    }

    public Task RetryStuckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RescanAsync(RequestRecord request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> RetryAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(true);
  }
}
