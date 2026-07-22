using System;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServerStrings"/>, which reads the catalogs embedded in the plugin assembly.
/// </summary>
public class ServerStringsTests
{
  [Fact]
  public void For_ReadsTheRequestedLanguage()
  {
    Assert.Equal("Available", ServerStrings.For("en")("notif_status_available"));
    Assert.Equal("Disponible", ServerStrings.For("fr")("notif_status_available"));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("auto")]     // "follow each user" is meaningless with no user in scope
  [InlineData("klingon")]
  public void For_FallsBackToEnglish_WhenTheLanguageIsUnusable(string? language)
  {
    Assert.Equal("Available", ServerStrings.For(language)("notif_status_available"));
  }

  [Fact]
  public void For_AcceptsALocaleAsWellAsALanguage()
  {
    Assert.Equal("Disponible", ServerStrings.For("fr-FR")("notif_status_available"));
    Assert.Equal("Disponible", ServerStrings.For("FR")("notif_status_available"));
  }

  [Fact]
  public void For_FallsBackToEnglish_ForAKeyMissingFromTheLanguage()
  {
    // Not a translation failure to hide: an untranslated string reads better than a blank one.
    var fr = ServerStrings.For("fr");
    Assert.NotEqual(string.Empty, fr("notif_status_available"));

    // A key no catalog has degrades to the key itself rather than to nothing.
    Assert.Equal("jc_no_such_key", fr("jc_no_such_key"));
  }

  [Fact]
  public void For_NeverReturnsNull()
  {
    Assert.Equal(string.Empty, ServerStrings.For("en")(null!));
  }

  [Fact]
  public void Normalize_ResolvesWhatTheConfigCanHold()
  {
    Assert.Equal("en", ServerStrings.Normalize("auto"));
    Assert.Equal("en", ServerStrings.Normalize(null));
    Assert.Equal("fr", ServerStrings.Normalize("fr"));
    Assert.Equal("fr", ServerStrings.Normalize("fr-BE"));
    Assert.Equal("en", ServerStrings.Normalize("english"));
  }

  [Fact]
  public void EveryNotificationKeyIsTranslated()
  {
    // The pages' i18n test guards catalog parity; this guards that the server half is actually filled
    // in, so no notification silently falls back to English once the language is set to French.
    string[] keys =
    {
      "notif_title_season", "notif_kind_show", "notif_kind_movie",
      "notif_created_subject", "notif_created_body", "notif_approved_subject", "notif_approved_body",
      "notif_denied_subject", "notif_denied_body", "notif_available_subject", "notif_available_body",
      "notif_failed_subject", "notif_failed_body", "notif_generic_subject", "notif_batch_body",
      "notif_test_subject", "notif_test_body", "notif_test_personal",
      "notif_status_pending", "notif_status_approved", "notif_status_available", "notif_status_denied",
      "notif_status_attention", "notif_field_requested_by", "notif_field_status", "notif_field_season",
      "notif_kind_show_label", "notif_kind_movie_label", "notif_view_on_tmdb", "notif_email_footer"
    };

    var en = ServerStrings.For("en");
    var fr = ServerStrings.For("fr");
    foreach (var key in keys)
    {
      Assert.NotEqual(key, en(key));                                    // present in English
      Assert.NotEqual(key, fr(key));                                    // present in French
      Assert.NotEqual(en(key), fr(key));                                // actually translated, not copied
    }
  }

  [Fact]
  public void PlaceholdersSurviveTranslation()
  {
    // A French sentence that lost its {title} would send a notification about nothing.
    foreach (var key in new[] { "notif_created_subject", "notif_created_body", "notif_title_season", "notif_batch_body" })
    {
      var english = ServerStrings.For("en")(key);
      var french = ServerStrings.For("fr")(key);
      foreach (var holder in new[] { "{title}", "{kind}", "{n}", "{episodes}" })
      {
        Assert.Equal(english.Contains(holder, StringComparison.Ordinal), french.Contains(holder, StringComparison.Ordinal));
      }
    }
  }
}
