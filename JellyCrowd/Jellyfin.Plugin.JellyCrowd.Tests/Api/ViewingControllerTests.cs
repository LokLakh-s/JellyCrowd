using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="ViewingController"/>.
/// </summary>
public class ViewingControllerTests
{
  private static readonly Guid Viewer = Guid.NewGuid();

  private sealed class FrenchCodes : LanguageCodes
  {
    public FrenchCodes()
      : base(Mock.Of<ILocalizationManager>(), Mock.Of<IServerConfigurationManager>())
    {
    }

    public override string? ThreeLetter(string? language) => language switch { "fr" => "fre", "en" => "eng", _ => null };

    public override string DubLanguage(PluginConfiguration config) => "fr";
  }

  private sealed class Fixture
  {
    public PluginConfiguration Config { get; } = new() { LanguagePreferencesEnabled = true, SubtitleDownloadsEnabled = true };

    public UserNotificationPrefs Stored { get; set; } = new() { UserId = Viewer, Email = "me@example.org" };

    public UserConfiguration Playback { get; } = new() { AudioLanguagePreference = "eng", PlayDefaultAudioTrack = true, SubtitleMode = SubtitlePlaybackMode.Default };

    public UserConfiguration? Written { get; private set; }

    public ViewingController Build()
    {
      var prefs = new Mock<IUserPrefsStore>();
      prefs.Setup(p => p.GetAsync(Viewer, It.IsAny<CancellationToken>())).ReturnsAsync(() => Stored);
      prefs.Setup(p => p.SetAsync(It.IsAny<UserNotificationPrefs>(), It.IsAny<CancellationToken>()))
        .Callback<UserNotificationPrefs, CancellationToken>((p, _) => Stored = p)
        .ReturnsAsync((UserNotificationPrefs p, CancellationToken _) => p);
      var user = new User("viewer", "Prov", "Prov");
      var users = new Mock<IUserManager>();
      users.Setup(u => u.GetUserById(Viewer)).Returns(user);
      users.Setup(u => u.GetUserDto(user, It.IsAny<string>())).Returns(new UserDto { Configuration = Playback });
      users.Setup(u => u.UpdateConfigurationAsync(Viewer, It.IsAny<UserConfiguration>()))
        .Callback<Guid, UserConfiguration>((_, c) => Written = c)
        .Returns(Task.CompletedTask);
      var accessor = Mock.Of<ICurrentUserAccessor>(a => a.GetUserIdAsync(It.IsAny<HttpRequest>()) == Task.FromResult(Viewer));
      return new ViewingController(prefs.Object, accessor, users.Object, new FrenchCodes(), () => Config, NullLogger<ViewingController>.Instance)
      {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
      };
    }
  }

  private static ViewingPrefsDto Post(string version, params string[] subtitles)
    => new() { LanguagePreference = version, SubtitleLanguages = new Collection<string>(subtitles) };

  [Fact]
  public async Task Mine_ReportsWhatIsOfferedAndTheDubLanguage()
  {
    var f = new Fixture();
    f.Stored.LanguagePreference = "dubbed";

    var dto = Assert.IsType<ViewingPrefsDto>(Assert.IsType<OkObjectResult>((await f.Build().Mine(CancellationToken.None)).Result).Value);

    Assert.True(dto.LanguagePreferencesAvailable);
    Assert.True(dto.SubtitleDownloadsAvailable);
    Assert.Equal("fr", dto.DubLanguage);
    Assert.Equal("dubbed", dto.LanguagePreference);
  }

  [Fact]
  public async Task SetMine_SavesAndKeepsTheNotificationSettings()
  {
    var f = new Fixture();

    var result = await f.Build().SetMine(Post("subtitled", "en", "EN", "fr"), CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal("subtitled", f.Stored.LanguagePreference);
    Assert.Equal(new[] { "en", "fr" }, f.Stored.SubtitleLanguages);
    Assert.Equal("me@example.org", f.Stored.Email);
  }

  [Fact]
  public async Task SetMine_Dubbed_SetsTheJellyfinAudioLanguage()
  {
    var f = new Fixture();

    await f.Build().SetMine(Post("dubbed"), CancellationToken.None);

    Assert.Equal("fre", f.Written!.AudioLanguagePreference);
    Assert.False(f.Written.PlayDefaultAudioTrack);
  }

  [Fact]
  public async Task SetMine_Subtitled_DefaultTrackAndAlwaysFrenchSubtitles()
  {
    var f = new Fixture();

    await f.Build().SetMine(Post("subtitled"), CancellationToken.None);

    Assert.Null(f.Written!.AudioLanguagePreference);
    Assert.True(f.Written.PlayDefaultAudioTrack);
    Assert.Equal("fre", f.Written.SubtitleLanguagePreference);
    Assert.Equal(SubtitlePlaybackMode.Always, f.Written.SubtitleMode);
  }

  [Fact]
  public async Task SetMine_NoPreference_LeavesTheAudioAlone()
  {
    var f = new Fixture();

    await f.Build().SetMine(Post(string.Empty, "en"), CancellationToken.None);

    Assert.Equal("eng", f.Written!.AudioLanguagePreference);
    Assert.Equal("eng", f.Written.SubtitleLanguagePreference);
    Assert.Equal(SubtitlePlaybackMode.Default, f.Written.SubtitleMode);
  }

  [Theory]
  [InlineData("vostfr")]
  [InlineData("dubbed", "french")]
  [InlineData("dubbed", "fr", "en", "es", "de", "it", "pt")]
  public async Task SetMine_InvalidInput_Returns400(string version, params string[] subtitles)
  {
    var f = new Fixture();

    var result = await f.Build().SetMine(Post(version, subtitles), CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Null(f.Written);
  }

  [Fact]
  public async Task SetMine_PartsNotOffered_AreLeftAsTheyWere()
  {
    var f = new Fixture();
    f.Config.LanguagePreferencesEnabled = false;
    f.Stored.LanguagePreference = "original";

    await f.Build().SetMine(Post("dubbed", "en"), CancellationToken.None);

    Assert.Equal("original", f.Stored.LanguagePreference);
    Assert.Equal(new[] { "en" }, f.Stored.SubtitleLanguages);
  }
}
