using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrProfileResolver"/>.
/// </summary>
public class ServarrProfileResolverTests
{
  private static readonly Guid Alice = Guid.NewGuid();
  private static readonly Guid Bob = Guid.NewGuid();

  private static PluginConfiguration Config(bool enabled = true) => new()
  {
    LanguagePreferencesEnabled = enabled,
    RadarrQualityProfileId = 4,
    RadarrProfileOriginal = 10,
    RadarrProfileDubbed = 11,
    SonarrQualityProfileId = 5,
    SonarrProfileOriginal = 20,
  };

  private static ServarrProfileResolver Create(IReadOnlyList<RequestRecord> requests, Dictionary<Guid, string> preferences, PluginConfiguration config)
  {
    var store = Mock.Of<IRequestStore>(s => s.GetAllAsync(It.IsAny<CancellationToken>()) == Task.FromResult(requests));
    var prefs = new Mock<IUserPrefsStore>();
    prefs.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((Guid id, CancellationToken _) => new UserNotificationPrefs { UserId = id, LanguagePreference = preferences.TryGetValue(id, out var p) ? p : string.Empty });
    return new ServarrProfileResolver(store, prefs.Object, () => config);
  }

  private static RequestRecord Request(Guid user, string mediaType = "movie", int tmdbId = 603, RequestStatus status = RequestStatus.Approved, int? season = null)
    => new() { UserId = user, MediaType = mediaType, TmdbId = tmdbId, Status = status, Season = season };

  [Fact]
  public async Task OneRequester_GetsTheirVersionsProfile()
  {
    var resolver = Create(new[] { Request(Alice) }, new() { [Alice] = "dubbed" }, Config());

    Assert.Equal(11, await resolver.ResolveAsync("movie", 603, CancellationToken.None));
  }

  [Fact]
  public async Task Disagreement_FallsBackToTheCommonProfile()
  {
    var resolver = Create(new[] { Request(Alice), Request(Bob) }, new() { [Alice] = "dubbed", [Bob] = "original" }, Config());

    Assert.Equal(4, await resolver.ResolveAsync("movie", 603, CancellationToken.None));
  }

  [Fact]
  public async Task DeniedRequestsAndOtherTitles_DoNotWeighIn()
  {
    var requests = new[]
    {
      Request(Alice),
      Request(Bob, status: RequestStatus.Denied),
      Request(Bob, tmdbId: 604),
    };
    var resolver = Create(requests, new() { [Alice] = "original", [Bob] = "dubbed" }, Config());

    Assert.Equal(10, await resolver.ResolveAsync("movie", 603, CancellationToken.None));
  }

  [Fact]
  public async Task Shows_AllSeasonsCountTogether()
  {
    // Sonarr has one profile per show: a requester of another season weighs in.
    var requests = new[] { Request(Alice, "tv", 1396, season: 1), Request(Bob, "tv", 1396, season: 2) };
    var resolver = Create(requests, new() { [Alice] = "original", [Bob] = "original" }, Config());

    Assert.Equal(20, await resolver.ResolveAsync("tv", 1396, CancellationToken.None));
  }

  [Fact]
  public async Task FeatureOff_NoSay()
  {
    var resolver = Create(new[] { Request(Alice) }, new() { [Alice] = "dubbed" }, Config(enabled: false));

    Assert.Null(await resolver.ResolveAsync("movie", 603, CancellationToken.None));
  }

  [Fact]
  public async Task NobodyWithAPreference_NoSay()
  {
    var resolver = Create(new[] { Request(Alice), Request(Bob) }, new(), Config());

    Assert.Null(await resolver.ResolveAsync("movie", 603, CancellationToken.None));
  }
}
