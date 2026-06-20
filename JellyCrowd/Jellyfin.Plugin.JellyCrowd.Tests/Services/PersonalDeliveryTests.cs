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
}
