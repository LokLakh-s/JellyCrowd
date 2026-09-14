using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestReconciler"/>, over a real <see cref="JsonRequestStore"/>.
/// </summary>
public sealed class RequestReconcilerTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-rec-" + Guid.NewGuid().ToString("N") + ".json");
  private readonly JsonRequestStore _store;
  private readonly Mock<ILibraryMatcher> _matcher = new();
  private readonly StubTmdbClient _tmdb = new();
  private readonly Jellyfin.Plugin.JellyCrowd.Configuration.PluginConfiguration _config = new();

  public RequestReconcilerTests() => _store = new JsonRequestStore(_path);

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private RequestReconciler Create() => new(
    _store,
    _matcher.Object,
    Mock.Of<INotificationService>(),
    Mock.Of<IDownloadDispatcher>(),
    _tmdb,
    () => _config,
    NullLogger<RequestReconciler>.Instance);

  private void LibraryHas(string? itemId)
    => _matcher.Setup(m => m.FindItemId(It.IsAny<string>(), It.IsAny<int>())).Returns(itemId);

  private async Task<RequestRecord> SeedAvailableAsync(string itemId)
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 42, MediaType = "movie", Title = "Shtisel" },
      CancellationToken.None);
    return (await _store.MarkAvailableAsync(created.Id, itemId, CancellationToken.None))!;
  }

  [Fact]
  public async Task Reconcile_ApprovedTitleNowInLibrary_BecomesAvailable()
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 42, MediaType = "movie", Title = "X", Status = RequestStatus.Approved },
      CancellationToken.None);
    LibraryHas("item-1");

    Assert.Equal(1, await Create().ReconcileAsync(CancellationToken.None));

    var stored = await _store.GetByIdAsync(created.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Available, stored!.Status);
    Assert.Equal("item-1", stored.JellyfinItemId);
  }

  // The reported case: the media is still there, but a library rescan / metadata refresh regenerated its
  // item id. The stored id then dangles and a later deletion silently finds nothing.
  [Fact]
  public async Task Reconcile_ItemIdChanged_RepointsTheRequest_WithoutRestartingOwnership()
  {
    var available = await SeedAvailableAsync("stale-id");
    var ownedSince = available.AvailableAt;
    LibraryHas("regenerated-id");

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(available.Id, CancellationToken.None);
    Assert.Equal("regenerated-id", stored!.JellyfinItemId);
    Assert.Equal(RequestStatus.Available, stored.Status); // still owned...
    Assert.Equal(ownedSince, stored.AvailableAt);         // ...and the expiry countdown did not restart
  }

  [Fact]
  public async Task Reconcile_ItemIdUnchanged_LeavesTheRequestAlone()
  {
    var available = await SeedAvailableAsync("item-1");
    LibraryHas("item-1");

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(available.Id, CancellationToken.None);
    Assert.Equal("item-1", stored!.JellyfinItemId);
    Assert.Equal(RequestStatus.Available, stored.Status);
  }

  // ---------- A season or a whole series is delivered once complete, not on its first episode ----------

  private static HashSet<EpisodeKey> SeasonKeys(int season, params int[] episodes)
  {
    var keys = new HashSet<EpisodeKey>();
    foreach (var episode in episodes)
    {
      keys.Add(new EpisodeKey(season, episode));
    }

    return keys;
  }

  // TMDB lists season 1 with three episodes, all aired.
  private void TmdbListsThreeAiredEpisodes()
    => _tmdb.EpisodesBySeason[1] = new List<Episode>
    {
      new() { SeasonNumber = 1, EpisodeNumber = 1, AirDate = "2020-01-01" },
      new() { SeasonNumber = 1, EpisodeNumber = 2, AirDate = "2020-01-08" },
      new() { SeasonNumber = 1, EpisodeNumber = 3, AirDate = "2020-01-15" },
    };

  private void LibraryHasEpisodes(HashSet<EpisodeKey> keys)
  {
    _matcher.Setup(m => m.FindEpisodeItemId(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>())).Returns(keys.Count > 0 ? "season-item" : null);
    _matcher.Setup(m => m.ListEpisodeKeys(It.IsAny<int>(), It.IsAny<int?>())).Returns(keys);
  }

  private Task<RequestRecord> SeedSeasonAsync(RequestStatus status, int presentEpisodes = 0, DateTime? progressAt = null, DateTime? availableAt = null)
    => _store.CreateAsync(
      new RequestRecord
      {
        UserId = Guid.NewGuid(),
        TmdbId = 7,
        MediaType = "tv",
        Title = "Show",
        Season = 1,
        Status = status,
        PresentEpisodes = presentEpisodes,
        ProgressAt = progressAt,
        AvailableAt = availableAt,
        JellyfinItemId = status == RequestStatus.Available ? "season-item" : null
      },
      CancellationToken.None);

  [Fact]
  public async Task Reconcile_Season_StaysApproved_WhileEpisodesAreStillArriving()
  {
    // The reported shape: one episode in, the rest downloading. Marking it available now released the
    // reservation for everything still to come.
    var request = await SeedSeasonAsync(RequestStatus.Approved);
    TmdbListsThreeAiredEpisodes();
    LibraryHasEpisodes(SeasonKeys(1, 1));

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, stored!.Status);
    Assert.Equal(1, stored.PresentEpisodes);
    Assert.NotNull(stored.ProgressAt);
  }

  [Fact]
  public async Task Reconcile_Season_BecomesAvailable_OnceEveryAiredEpisodeIsThere()
  {
    var request = await SeedSeasonAsync(RequestStatus.Approved);
    TmdbListsThreeAiredEpisodes();
    LibraryHasEpisodes(SeasonKeys(1, 1, 2, 3));

    Assert.Equal(1, await Create().ReconcileAsync(CancellationToken.None));
    Assert.Equal(RequestStatus.Available, (await _store.GetByIdAsync(request.Id, CancellationToken.None))!.Status);
  }

  [Fact]
  public async Task Reconcile_Season_SettlesForWhatIsThere_AfterTheGracePeriod()
  {
    // Episode 3 will never be found: the season must not stay pending forever.
    var request = await SeedSeasonAsync(RequestStatus.Approved, presentEpisodes: 2, progressAt: DateTime.UtcNow.AddHours(-72));
    TmdbListsThreeAiredEpisodes();
    LibraryHasEpisodes(SeasonKeys(1, 1, 2));

    await Create().ReconcileAsync(CancellationToken.None);

    Assert.Equal(RequestStatus.Available, (await _store.GetByIdAsync(request.Id, CancellationToken.None))!.Status);
  }

  [Fact]
  public async Task Reconcile_Season_ANewArrival_RestartsTheGracePeriod()
  {
    // Still trickling in: the last arrival was old, but a new episode just landed — keep waiting.
    var request = await SeedSeasonAsync(RequestStatus.Approved, presentEpisodes: 1, progressAt: DateTime.UtcNow.AddHours(-72));
    TmdbListsThreeAiredEpisodes();
    LibraryHasEpisodes(SeasonKeys(1, 1, 2));

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, stored!.Status);
    Assert.Equal(2, stored.PresentEpisodes);
    Assert.True(stored.ProgressAt > DateTime.UtcNow.AddMinutes(-5));
  }

  [Fact]
  public async Task Reconcile_Season_WhenTmdbCannotListEpisodes_OnlyTheGracePeriodSettlesIt()
  {
    // No TMDB episodes: completeness is unknown, so a fresh arrival waits and an old one settles.
    var fresh = await SeedSeasonAsync(RequestStatus.Approved);
    LibraryHasEpisodes(SeasonKeys(1, 1));
    await Create().ReconcileAsync(CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, (await _store.GetByIdAsync(fresh.Id, CancellationToken.None))!.Status);

    var settled = await SeedSeasonAsync(RequestStatus.Approved, presentEpisodes: 1, progressAt: DateTime.UtcNow.AddDays(-3));
    await Create().ReconcileAsync(CancellationToken.None);
    Assert.Equal(RequestStatus.Available, (await _store.GetByIdAsync(settled.Id, CancellationToken.None))!.Status);
  }

  [Fact]
  public async Task Reconcile_AvailableSeason_ANewEpisode_RestartsTheOwnershipClock()
  {
    // Otherwise the newest episode would expire along with the first one, weeks early.
    var ownedSince = DateTime.UtcNow.AddDays(-80);
    var request = await SeedSeasonAsync(RequestStatus.Available, presentEpisodes: 2, availableAt: ownedSince);
    LibraryHasEpisodes(SeasonKeys(1, 1, 2, 3));

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Equal(3, stored!.PresentEpisodes);
    Assert.True(stored.AvailableAt > DateTime.UtcNow.AddMinutes(-5));
  }

  [Fact]
  public async Task Reconcile_AvailableSeason_WithoutABaseline_RecordsOneWithoutRestartingTheClock()
  {
    // A season fulfilled before arrivals were tracked: the first sweep must not push its expiry back.
    var ownedSince = DateTime.UtcNow.AddDays(-80);
    var request = await SeedSeasonAsync(RequestStatus.Available, presentEpisodes: 0, availableAt: ownedSince);
    LibraryHasEpisodes(SeasonKeys(1, 1, 2, 3));

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Equal(3, stored!.PresentEpisodes);
    Assert.Equal(ownedSince, stored.AvailableAt);
  }

  [Fact]
  public async Task Reconcile_MediaGone_RevertsToApproved()
  {
    var available = await SeedAvailableAsync("item-1");
    LibraryHas(null);

    await Create().ReconcileAsync(CancellationToken.None);

    var stored = await _store.GetByIdAsync(available.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, stored!.Status);
  }
}
