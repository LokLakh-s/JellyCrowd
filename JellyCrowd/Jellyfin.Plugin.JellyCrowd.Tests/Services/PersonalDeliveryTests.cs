using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="PersonalDelivery"/>.
/// </summary>
public class PersonalDeliveryTests
{
  private static PluginConfiguration SmtpConfig() => new() { SmtpHost = "smtp.example", SmtpFromAddress = "jc@example" };

  [Fact]
  public void ShouldEmail_TrueWhenEnabledEmailAndSmtpConfigured()
    => Assert.True(PersonalDelivery.ShouldEmail(new UserNotificationPrefs { Enabled = true, Email = "u@example" }, SmtpConfig()));

  [Fact]
  public void ShouldEmail_FalseWhenDisabledOrNoEmailOrNoSmtp()
  {
    Assert.False(PersonalDelivery.ShouldEmail(new UserNotificationPrefs { Enabled = false, Email = "u@example" }, SmtpConfig()));
    Assert.False(PersonalDelivery.ShouldEmail(new UserNotificationPrefs { Enabled = true, Email = "" }, SmtpConfig()));
    Assert.False(PersonalDelivery.ShouldEmail(new UserNotificationPrefs { Enabled = true, Email = "u@example" }, new PluginConfiguration()));
  }

  [Fact]
  public void NtfyUrl_UsesConfiguredServerThenDefault()
  {
    var prefs = new UserNotificationPrefs { Enabled = true, NtfyTopic = "my-topic" };

    Assert.Equal("https://ntfy.sh/my-topic", PersonalDelivery.NtfyUrl(prefs, new PluginConfiguration()));
    Assert.Equal("https://n.example/my-topic", PersonalDelivery.NtfyUrl(prefs, new PluginConfiguration { NtfyServer = "https://n.example/" }));
  }

  [Fact]
  public void NtfyUrl_NullWhenDisabledOrNoTopic()
  {
    Assert.Null(PersonalDelivery.NtfyUrl(new UserNotificationPrefs { Enabled = false, NtfyTopic = "t" }, new PluginConfiguration()));
    Assert.Null(PersonalDelivery.NtfyUrl(new UserNotificationPrefs { Enabled = true, NtfyTopic = "" }, new PluginConfiguration()));
  }

  [Fact]
  public void KindFor_AvailableSplitsByWhetherRequestedBeforeRelease()
  {
    var requestedAt = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

    // Deferred far past request time -> requested before release.
    var unreleased = new RequestRecord { RequestedAt = requestedAt, DesiredAt = requestedAt.AddDays(30) };
    Assert.Equal(PersonalNotifyKind.AvailableUnreleased, PersonalDelivery.KindFor(NotificationEvent.Available, unreleased));

    // No deferral (released title) -> ordinary available.
    var released = new RequestRecord { RequestedAt = requestedAt, DesiredAt = requestedAt };
    Assert.Equal(PersonalNotifyKind.AvailableReleased, PersonalDelivery.KindFor(NotificationEvent.Available, released));
  }

  [Fact]
  public void KindFor_DecisionsAndNone()
  {
    var r = new RequestRecord();
    Assert.Equal(PersonalNotifyKind.Decision, PersonalDelivery.KindFor(NotificationEvent.Approved, r));
    Assert.Equal(PersonalNotifyKind.Decision, PersonalDelivery.KindFor(NotificationEvent.Denied, r));
    Assert.Equal(PersonalNotifyKind.Decision, PersonalDelivery.KindFor(NotificationEvent.Failed, r));
    Assert.Equal(PersonalNotifyKind.None, PersonalDelivery.KindFor(NotificationEvent.Created, r));
  }

  [Fact]
  public void IsKindEnabled_HonoursPerCategoryOptInAndDefaultsOff()
  {
    Assert.False(PersonalDelivery.IsKindEnabled(new UserNotificationPrefs(), PersonalNotifyKind.AvailableUnreleased));
    Assert.False(PersonalDelivery.IsKindEnabled(new UserNotificationPrefs(), PersonalNotifyKind.Decision));
    Assert.True(PersonalDelivery.IsKindEnabled(new UserNotificationPrefs { NotifyAvailableUnreleased = true }, PersonalNotifyKind.AvailableUnreleased));
    Assert.False(PersonalDelivery.IsKindEnabled(new UserNotificationPrefs { NotifyAvailableUnreleased = true }, PersonalNotifyKind.AvailableReleased));
    Assert.True(PersonalDelivery.IsKindEnabled(new UserNotificationPrefs { NotifyQuotaExpiry = true }, PersonalNotifyKind.QuotaExpiry));
    Assert.False(PersonalDelivery.IsKindEnabled(new UserNotificationPrefs { NotifyDecisions = true }, PersonalNotifyKind.None));
  }
}
