using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="AvailabilityFollowUp"/>.
/// </summary>
public class AvailabilityFollowUpTests
{
  private static readonly Guid Viewer = Guid.NewGuid();

  // Two- and three-letter codes for the few languages these tests use, as Jellyfin's culture table has them.
  private sealed class FakeCodes : LanguageCodes
  {
    private static readonly Dictionary<string, (string Two, string Three)> Table = new(StringComparer.OrdinalIgnoreCase)
    {
      ["fr"] = ("fr", "fre"), ["fre"] = ("fr", "fre"), ["fra"] = ("fr", "fre"),
      ["en"] = ("en", "eng"), ["eng"] = ("en", "eng"),
    };

    public FakeCodes()
      : base(Mock.Of<ILocalizationManager>(), Mock.Of<IServerConfigurationManager>())
    {
    }

    public override string? TwoLetter(string? language) => language is not null && Table.TryGetValue(language, out var c) ? c.Two : null;

    public override string? ThreeLetter(string? language) => language is not null && Table.TryGetValue(language, out var c) ? c.Three : null;

    public override string DisplayName(string language) => language;

    public override string DubLanguage(PluginConfiguration config) => "fr";
  }

  private sealed class Fixture
  {
    public PluginConfiguration Config { get; } = new() { LanguagePreferencesEnabled = true, SubtitleDownloadsEnabled = true };

    public UserNotificationPrefs Prefs { get; } = new() { UserId = Viewer };

    public Movie Movie { get; } = new() { Id = Guid.NewGuid(), Name = "The Matrix" };

    public List<MediaStream> Streams { get; } = new();

    public string OriginalLanguage { get; set; } = "en";

    public List<(string Language, string Id)> Downloaded { get; } = new();

    public Dictionary<string, RemoteSubtitleInfo[]> Results { get; } = new(StringComparer.Ordinal);

    public RecordingNotificationService Notifications { get; } = new();

    public AvailabilityFollowUp Build()
    {
      var prefs = new Mock<IUserPrefsStore>();
      prefs.Setup(p => p.GetAsync(Viewer, It.IsAny<CancellationToken>())).ReturnsAsync(Prefs);
      var matcher = new Mock<ILibraryMatcher>();
      matcher.Setup(m => m.FindItemId("movie", 603)).Returns(Movie.Id.ToString("N"));
      var library = new Mock<ILibraryManager>();
      library.Setup(l => l.GetItemById(Movie.Id)).Returns(Movie);
      var subtitles = new Mock<ISubtitleManager>();
      subtitles.Setup(s => s.SearchSubtitles(It.IsAny<Video>(), It.IsAny<string>(), null, true, It.IsAny<CancellationToken>()))
        .ReturnsAsync((Video _, string lang, bool? _, bool _, CancellationToken _) => Results.TryGetValue(lang, out var r) ? r : Array.Empty<RemoteSubtitleInfo>());
      subtitles.Setup(s => s.DownloadSubtitles(It.IsAny<Video>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Callback<Video, string, CancellationToken>((_, id, _) => Downloaded.Add((Results.First(r => r.Value.Any(i => i.Id == id)).Key, id)))
        .Returns(Task.CompletedTask);
      var tmdb = new StubTmdbClient { Details = new CatalogItem { TmdbId = 603, MediaType = "movie", OriginalLanguage = OriginalLanguage } };
      return new AvailabilityFollowUp(
        () => Config,
        prefs.Object,
        matcher.Object,
        library.Object,
        subtitles.Object,
        new FakeCodes(),
        tmdb,
        Notifications,
        NullLogger<AvailabilityFollowUp>.Instance,
        _ => Streams,
        TimeSpan.Zero);
    }

    public Task Run() => Build().ProcessAsync(new[] { new RequestRecord { UserId = Viewer, TmdbId = 603, MediaType = "movie", Title = "The Matrix", Status = RequestStatus.Available } }, CancellationToken.None);
  }

  private static MediaStream Audio(string? language) => new() { Type = MediaStreamType.Audio, Language = language };

  private static MediaStream Subtitle(string language) => new() { Type = MediaStreamType.Subtitle, Language = language };

  [Fact]
  public async Task MissingSubtitleLanguages_AreFetched_PresentOnesAreNot()
  {
    var f = new Fixture();
    f.Prefs.SubtitleLanguages.Add("fr");
    f.Prefs.SubtitleLanguages.Add("en");
    f.Streams.Add(Audio("eng"));
    f.Streams.Add(Subtitle("eng"));
    f.Results["fre"] = new[] { new RemoteSubtitleInfo { Id = "fr-1", DownloadCount = 10 } };
    f.Results["eng"] = new[] { new RemoteSubtitleInfo { Id = "en-1" } };

    await f.Run();

    Assert.Equal(new[] { ("fre", "fr-1") }, f.Downloaded);
  }

  [Fact]
  public async Task OriginalPreferred_OnlyTheDubDelivered_GetsTheShortNotice()
  {
    var f = new Fixture();
    f.Prefs.LanguagePreference = "original";
    f.Streams.Add(Audio("fre"));

    await f.Run();

    var notice = Assert.Single(f.Notifications.Personal);
    Assert.Equal(Viewer, notice.UserId);
    Assert.Equal("The Matrix", notice.Title);
  }

  [Fact]
  public async Task SubtitledPreferred_FetchesTheDubLanguageSubtitles_AndThenSaysNothing()
  {
    var f = new Fixture();
    f.Prefs.LanguagePreference = "subtitled";
    f.Streams.Add(Audio("eng"));
    f.Results["fre"] = new[] { new RemoteSubtitleInfo { Id = "fr-1" } };

    await f.Run();

    Assert.Equal(new[] { ("fre", "fr-1") }, f.Downloaded);
    Assert.Empty(f.Notifications.Personal);
  }

  [Fact]
  public async Task SubtitledPreferred_NoSubtitleFound_GetsTheNotice()
  {
    var f = new Fixture();
    f.Prefs.LanguagePreference = "subtitled";
    f.Streams.Add(Audio("eng"));

    await f.Run();

    Assert.Single(f.Notifications.Personal);
  }

  [Fact]
  public async Task PreferenceMet_NoNotice()
  {
    var f = new Fixture();
    f.Prefs.LanguagePreference = "original";
    f.Streams.Add(Audio("eng"));
    f.Streams.Add(Audio("fre"));

    await f.Run();

    Assert.Empty(f.Notifications.Personal);
  }

  [Fact]
  public async Task UntaggedTrack_NoNotice()
  {
    var f = new Fixture();
    f.Prefs.LanguagePreference = "dubbed";
    f.Streams.Add(Audio(null));

    await f.Run();

    Assert.Empty(f.Notifications.Personal);
  }

  [Fact]
  public async Task FeaturesOff_NothingHappens()
  {
    var f = new Fixture();
    f.Config.LanguagePreferencesEnabled = false;
    f.Config.SubtitleDownloadsEnabled = false;
    f.Prefs.LanguagePreference = "original";
    f.Prefs.SubtitleLanguages.Add("fr");
    f.Streams.Add(Audio("fre"));
    f.Results["fre"] = new[] { new RemoteSubtitleInfo { Id = "fr-1" } };

    await f.Run();

    Assert.Empty(f.Downloaded);
    Assert.Empty(f.Notifications.Personal);
  }
}
