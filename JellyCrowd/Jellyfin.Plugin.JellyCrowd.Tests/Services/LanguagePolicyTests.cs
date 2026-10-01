using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Model.Providers;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="LanguagePolicy"/> and <see cref="SubtitlePicker"/>.
/// </summary>
public class LanguagePolicyTests
{
  private const int Common = 4;

  private static int Profiles(string preference) => LanguagePolicy.ProfileFor(preference, 10, 11, 12, Common);

  [Theory]
  [InlineData("original", "original")]
  [InlineData(" Dubbed ", "dubbed")]
  [InlineData("subtitled", "subtitled")]
  [InlineData("vostfr", "")]
  [InlineData(null, "")]
  public void Normalize_KeepsTheThreeVersions(string? raw, string expected)
  {
    Assert.Equal(expected, LanguagePolicy.Normalize(raw));
  }

  [Fact]
  public void ProfileFor_UnsetProfile_FallsBackToTheCommonOne()
  {
    Assert.Equal(11, Profiles("dubbed"));
    Assert.Equal(Common, LanguagePolicy.ProfileFor("dubbed", 10, 0, 12, Common));
    Assert.Equal(Common, Profiles(string.Empty));
  }

  [Fact]
  public void TargetProfile_RequestersAgree_TheirProfile()
  {
    Assert.Equal(10, LanguagePolicy.TargetProfile(new[] { "original", "original" }, Profiles, Common));
  }

  [Fact]
  public void TargetProfile_RequestersDisagree_TheCommonProfile()
  {
    Assert.Equal(Common, LanguagePolicy.TargetProfile(new[] { "original", "dubbed" }, Profiles, Common));
  }

  [Fact]
  public void TargetProfile_RequestersWithoutPreference_DoNotWeighIn()
  {
    Assert.Equal(11, LanguagePolicy.TargetProfile(new[] { string.Empty, "dubbed", null }, Profiles, Common));
    Assert.Null(LanguagePolicy.TargetProfile(new[] { string.Empty, null }, Profiles, Common)); // no say at all
  }

  [Fact]
  public void TargetProfile_TwoVersionsSharingAProfile_Agree()
  {
    Assert.Equal(10, LanguagePolicy.TargetProfile(new[] { "original", "subtitled" }, p => LanguagePolicy.ProfileFor(p, 10, 11, 10, Common), Common));
  }

  [Theory]
  [InlineData("original", "en", new[] { "fr" }, new string[0], LanguageNotice.DubbedOnly)]
  [InlineData("original", "en", new[] { "fr", "en" }, new string[0], LanguageNotice.None)]
  [InlineData("dubbed", "en", new[] { "en" }, new string[0], LanguageNotice.OriginalOnly)]
  [InlineData("dubbed", "en", new[] { "en", "fr" }, new string[0], LanguageNotice.None)]
  [InlineData("subtitled", "en", new[] { "en" }, new string[0], LanguageNotice.NoSubtitles)]
  [InlineData("subtitled", "en", new[] { "en" }, new[] { "fr" }, LanguageNotice.None)]
  [InlineData("subtitled", "en", new[] { "fr" }, new string[0], LanguageNotice.DubbedOnly)]
  [InlineData("original", "fr", new[] { "fr" }, new string[0], LanguageNotice.None)] // French film: VO = VF
  [InlineData("", "en", new[] { "fr" }, new string[0], LanguageNotice.None)]
  [InlineData("original", "en", new[] { "es" }, new string[0], LanguageNotice.None)] // neither: not one of the three cases
  public void Evaluate_TheThreeShortNotices(string preference, string original, string[] audio, string[] subtitles, LanguageNotice expected)
  {
    Assert.Equal(expected, LanguagePolicy.Evaluate(preference, original, audio, subtitles, "fr"));
  }

  [Fact]
  public void Evaluate_UntaggedTrackOrUnknownOriginal_SaysNothing()
  {
    Assert.Equal(LanguageNotice.None, LanguagePolicy.Evaluate("original", "en", new string?[] { "fr", null }, Array.Empty<string>(), "fr"));
    Assert.Equal(LanguageNotice.None, LanguagePolicy.Evaluate("original", null, new[] { "fr" }, Array.Empty<string>(), "fr"));
    Assert.Equal(LanguageNotice.None, LanguagePolicy.Evaluate("original", "en", Array.Empty<string>(), Array.Empty<string>(), "fr"));
  }

  [Fact]
  public void SubtitlesWanted_SubtitledAddsTheDubLanguageFirst()
  {
    Assert.Equal(new[] { "fr", "en" }, LanguagePolicy.SubtitlesWanted("subtitled", new[] { "EN", "fr" }, "fr"));
    Assert.Equal(new[] { "en" }, LanguagePolicy.SubtitlesWanted("original", new[] { "en", " " }, "fr"));
    Assert.Empty(LanguagePolicy.SubtitlesWanted(string.Empty, null, "fr"));
  }

  [Fact]
  public void PlaybackFor_EachVersion()
  {
    Assert.Equal(new PlaybackSettings(true, "fre", false, null, false), LanguagePolicy.PlaybackFor("dubbed", "fre", null));
    Assert.Equal(new PlaybackSettings(true, null, true, "eng", false), LanguagePolicy.PlaybackFor("original", "fre", "eng"));
    Assert.Equal(new PlaybackSettings(true, null, true, "fre", true), LanguagePolicy.PlaybackFor("subtitled", "fre", "eng"));
  }

  [Fact]
  public void PlaybackFor_NoPreference_LeavesTheAudioAlone()
  {
    var settings = LanguagePolicy.PlaybackFor(string.Empty, "fre", "eng");

    Assert.False(settings.SetAudio);
    Assert.Equal("eng", settings.SubtitleLanguage);
    Assert.False(settings.AlwaysSubtitles);
  }

  [Fact]
  public void SubtitlePicker_SkipsMachineTranslatedAndForced_PrefersAnExactMatch()
  {
    var picked = SubtitlePicker.Pick(new List<RemoteSubtitleInfo>
    {
      new() { Id = "ai", AiTranslated = true, DownloadCount = 9999 },
      new() { Id = "mt", MachineTranslated = true, DownloadCount = 9999 },
      new() { Id = "forced", Forced = true, DownloadCount = 9999 },
      new() { Id = "popular", DownloadCount = 500 },
      new() { Id = "hash", IsHashMatch = true, DownloadCount = 3 },
    });

    Assert.Equal("hash", picked!.Id);
  }

  [Fact]
  public void SubtitlePicker_RegularBeforeHearingImpaired_ThenMostDownloaded()
  {
    var picked = SubtitlePicker.Pick(new List<RemoteSubtitleInfo>
    {
      new() { Id = "sdh", HearingImpaired = true, DownloadCount = 900 },
      new() { Id = "few", DownloadCount = 10 },
      new() { Id = "many", DownloadCount = 300 },
    });

    Assert.Equal("many", picked!.Id);
  }

  [Fact]
  public void SubtitlePicker_NothingAcceptable_IsNull()
  {
    Assert.Null(SubtitlePicker.Pick(new List<RemoteSubtitleInfo> { new() { Id = "ai", AiTranslated = true } }));
    Assert.Null(SubtitlePicker.Pick(null));
  }
}
