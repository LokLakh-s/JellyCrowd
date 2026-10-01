using System;
using System.Collections.Generic;
using System.Linq;
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
/// Tests for <see cref="NextSeasonRequester"/>.
/// </summary>
public class NextSeasonRequesterTests
{
  private const int Show = 1396;
  private static readonly Guid Viewer = Guid.NewGuid();
  private static readonly DateTime Today = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

  private sealed class Fixture
  {
    public PluginConfiguration Config { get; } = new() { AutoNextSeasonEnabled = true, AutoNextSeasonEpisodesLeft = 2 };

    public UserNotificationPrefs Prefs { get; } = new() { UserId = Viewer, AutoRequestNextSeason = true };

    public List<Season> Seasons { get; } = new()
    {
      new Season { SeasonNumber = 1, EpisodeCount = 10 },
      new Season { SeasonNumber = 2, EpisodeCount = 8 },
    };

    public List<Episode> NextEpisodes { get; } = new()
    {
      new Episode { SeasonNumber = 2, EpisodeNumber = 1, AirDate = "2026-01-01" },
    };

    public bool NextSeasonInLibrary { get; set; }

    public FakeCreator Creator { get; } = new();

    public FakeLedger Ledger { get; } = new();

    public RecordingNotificationService Notifications { get; } = new();

    public NextSeasonRequester Build()
    {
      var prefs = new Mock<IUserPrefsStore>();
      prefs.Setup(p => p.GetAsync(Viewer, It.IsAny<CancellationToken>())).ReturnsAsync(Prefs);
      var structure = new Mock<ISeriesStructureProvider>();
      structure.Setup(s => s.GetSeasonsAsync(Show, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Seasons);
      structure.Setup(s => s.GetEpisodesAsync(Show, 2, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(NextEpisodes);
      var library = new Mock<ILibraryMatcher>();
      library.Setup(l => l.FindEpisodeItemId(Show, 2, null)).Returns(() => NextSeasonInLibrary ? "s2" : null);
      var tmdb = new StubTmdbClient { Details = new CatalogItem { TmdbId = Show, MediaType = "tv", PosterPath = "/poster.jpg" } };
      return new NextSeasonRequester(
        () => Config,
        prefs.Object,
        structure.Object,
        library.Object,
        tmdb,
        Creator,
        Ledger,
        Notifications,
        new NoOpActivityLog(),
        _ => "viewer",
        NullLogger<NextSeasonRequester>.Instance,
        () => Today);
    }
  }

  internal sealed class FakeCreator : IRequestCreationService
  {
    public List<(Guid UserId, CreateRequestDto Dto)> Calls { get; } = new();

    public RequestCreationOutcome Outcome { get; set; } = RequestCreationOutcome.Created;

    public Task<RequestCreationResult> CreateAsync(Guid userId, CreateRequestDto dto, CancellationToken cancellationToken)
    {
      Calls.Add((userId, dto));
      return Task.FromResult(Outcome == RequestCreationOutcome.Created
        ? RequestCreationResult.Created(new RequestRecord { UserId = userId, TmdbId = dto.TmdbId, MediaType = dto.MediaType, Season = dto.Season, Episode = dto.Episode })
        : RequestCreationResult.Refused(Outcome, "refused"));
    }

    public Task<RequestCreationResult> CreateOnBehalfAsync(AdminCreateRequestDto dto, CancellationToken cancellationToken)
      => throw new NotSupportedException();

    public Task<RequestCreationResult> CreateForChildAsync(Guid parentId, Guid childId, CreateRequestDto dto, CancellationToken cancellationToken)
      => throw new NotSupportedException();
  }

  internal sealed class FakeLedger : IAutoRequestLedger
  {
    public HashSet<(Guid, int, int)> Entries { get; } = new();

    public Task<bool> ContainsAsync(Guid userId, int tmdbId, int season, CancellationToken cancellationToken)
      => Task.FromResult(Entries.Contains((userId, tmdbId, season)));

    public Task RecordAsync(Guid userId, int tmdbId, int season, CancellationToken cancellationToken)
    {
      Entries.Add((userId, tmdbId, season));
      return Task.CompletedTask;
    }
  }

  private static Task<NextSeasonOutcome> Play(Fixture f, int season, int episode)
    => f.Build().ConsiderAsync(Viewer, Show, "Breaking Bad", season, episode, CancellationToken.None);

  [Fact]
  public async Task NearTheEnd_RequestsTheNextSeasonThroughTheNormalPath()
  {
    var f = new Fixture();

    var outcome = await Play(f, 1, 8);

    Assert.Equal(NextSeasonOutcome.Requested, outcome);
    var call = Assert.Single(f.Creator.Calls);
    Assert.Equal(Viewer, call.UserId);
    Assert.Equal(Show, call.Dto.TmdbId);
    Assert.Equal("tv", call.Dto.MediaType);
    Assert.Equal("Breaking Bad", call.Dto.Title);
    Assert.Equal("/poster.jpg", call.Dto.PosterPath);
    Assert.Equal(2, call.Dto.Season);
    Assert.Null(call.Dto.Episode); // aired season: requested whole
    Assert.Contains((Viewer, Show, 2), f.Ledger.Entries);
    Assert.Contains(f.Notifications.Personal, n => n.UserId == Viewer && n.Kind == PersonalNotifyKind.Decision);
  }

  [Fact]
  public async Task NextSeasonStillAiring_IsRequestedEpisodeByEpisode()
  {
    var f = new Fixture();
    f.NextEpisodes.Clear();
    f.NextEpisodes.Add(new Episode { SeasonNumber = 2, EpisodeNumber = 1, AirDate = "2026-09-24" });
    f.NextEpisodes.Add(new Episode { SeasonNumber = 2, EpisodeNumber = 2, AirDate = "2026-10-08" });

    await Play(f, 1, 9);

    Assert.Equal(new int?[] { 1, 2 }, f.Creator.Calls.Select(c => c.Dto.Episode));
    Assert.Equal("2026-10-08", f.Creator.Calls[1].Dto.ReleaseDate);
  }

  [Fact]
  public async Task FarFromTheEnd_RequestsNothing()
  {
    var f = new Fixture();

    Assert.Equal(NextSeasonOutcome.NotYet, await Play(f, 1, 3));
    Assert.Empty(f.Creator.Calls);
    Assert.Empty(f.Ledger.Entries);
  }

  [Fact]
  public async Task FeatureOff_RequestsNothing()
  {
    var f = new Fixture();
    f.Config.AutoNextSeasonEnabled = false;

    Assert.Equal(NextSeasonOutcome.Disabled, await Play(f, 1, 10));
    Assert.Empty(f.Creator.Calls);
  }

  [Fact]
  public async Task UserNotOptedIn_RequestsNothing()
  {
    var f = new Fixture();
    f.Prefs.AutoRequestNextSeason = false;

    Assert.Equal(NextSeasonOutcome.NotOptedIn, await Play(f, 1, 10));
    Assert.Empty(f.Creator.Calls);
  }

  [Fact]
  public async Task AlreadyHandled_IsNotRequestedAgain()
  {
    // A season the user cancelled, or an admin denied, must not come back on the next episode.
    var f = new Fixture();
    f.Ledger.Entries.Add((Viewer, Show, 2));

    Assert.Equal(NextSeasonOutcome.AlreadyHandled, await Play(f, 1, 9));
    Assert.Empty(f.Creator.Calls);
  }

  [Fact]
  public async Task NextSeasonAlreadyInTheLibrary_IsNotRequested()
  {
    // A rewatch of an old season must not charge the following ones to the user's quota.
    var f = new Fixture { NextSeasonInLibrary = true };

    Assert.Equal(NextSeasonOutcome.AlreadyInLibrary, await Play(f, 1, 10));
    Assert.Empty(f.Creator.Calls);
    Assert.Contains((Viewer, Show, 2), f.Ledger.Entries);
  }

  [Fact]
  public async Task RefusedByTheNormalRules_IsRecordedAndNotAnnounced()
  {
    var f = new Fixture();
    f.Creator.Outcome = RequestCreationOutcome.RateLimited;

    Assert.Equal(NextSeasonOutcome.Refused, await Play(f, 1, 10));
    Assert.Contains((Viewer, Show, 2), f.Ledger.Entries);
    Assert.Empty(f.Notifications.Personal);
  }

  [Fact]
  public async Task CheckUnavailable_IsLeftOpenForALaterEpisode()
  {
    var f = new Fixture();
    f.Creator.Outcome = RequestCreationOutcome.Unavailable;

    Assert.Equal(NextSeasonOutcome.Unavailable, await Play(f, 1, 9));
    Assert.Empty(f.Ledger.Entries);
  }
}
