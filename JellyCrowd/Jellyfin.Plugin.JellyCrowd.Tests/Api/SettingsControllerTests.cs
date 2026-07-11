using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="SettingsController"/>.
/// </summary>
public class SettingsControllerTests
{
  private static LanguageSettingDto GetLanguage(string? configured)
  {
    var controller = new SettingsController(() => new PluginConfiguration { Language = configured! }, Mock.Of<ICurrentUserAccessor>(), Mock.Of<ILibraryManager>(), Mock.Of<IIntroFileRegistry>());
    var result = controller.GetLanguage();
    var ok = Assert.IsType<OkObjectResult>(result.Result);
    return Assert.IsType<LanguageSettingDto>(ok.Value);
  }

  [Fact]
  public void GetLanguage_ReturnsConfiguredLanguage()
  {
    Assert.Equal("fr", GetLanguage("fr").Language);
    Assert.Equal("auto", GetLanguage("auto").Language);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  public void GetLanguage_FallsBackToAuto_WhenUnset(string? configured)
  {
    Assert.Equal("auto", GetLanguage(configured).Language);
  }

  private static LanguageSettingDto GetLanguageFor(PluginConfiguration config)
  {
    var controller = new SettingsController(() => config, Mock.Of<ICurrentUserAccessor>(), Mock.Of<ILibraryManager>(), Mock.Of<IIntroFileRegistry>());
    var ok = Assert.IsType<OkObjectResult>(controller.GetLanguage().Result);
    return Assert.IsType<LanguageSettingDto>(ok.Value);
  }

  [Fact]
  public void GetLanguage_ExposesGuideLink_OnlyWhenEnabled()
  {
    var dto = GetLanguageFor(new PluginConfiguration
    {
      GuideLinkEnabled = true,
      GuideLinkUrl = "https://keeklah.fr/jellycrowd-guide"
    });
    Assert.Equal("https://keeklah.fr/jellycrowd-guide", dto.GuideLinkUrl);
  }

  [Fact]
  public void GetLanguage_HidesGuideLink_WhenConfiguredButDisabled()
  {
    // A disabled-but-configured URL stays private, like the Discord/Support links.
    var dto = GetLanguageFor(new PluginConfiguration
    {
      GuideLinkEnabled = false,
      GuideLinkUrl = "https://keeklah.fr/jellycrowd-guide"
    });
    Assert.Equal(string.Empty, dto.GuideLinkUrl);
  }

  private static BrandingDto GetBranding(PluginConfiguration config)
  {
    var controller = new SettingsController(() => config, Mock.Of<ICurrentUserAccessor>(), Mock.Of<ILibraryManager>(), Mock.Of<IIntroFileRegistry>());
    var ok = Assert.IsType<OkObjectResult>(controller.GetBranding().Result);
    return Assert.IsType<BrandingDto>(ok.Value);
  }

  [Fact]
  public void GetBranding_DefaultsAreOffAndEmpty()
  {
    var dto = GetBranding(new PluginConfiguration());
    Assert.False(dto.Enabled);
    Assert.Equal(string.Empty, dto.LogoUrl);
    Assert.Equal(string.Empty, dto.AccentColor);
    Assert.Equal(string.Empty, dto.CustomCss);
    Assert.False(dto.PresetCompactEpisodes);
    Assert.Empty(dto.DrawerLinks);
  }

  [Fact]
  public void GetBranding_ReflectsConfiguredValuesAndDrawerLinks()
  {
    var config = new PluginConfiguration
    {
      BrandingEnabled = true,
      BrandingLogoUrl = "https://x/logo.png",
      BrandingAccentColor = "#ff0000",
      BrandingFontFamily = "Inter",
      BrandingCustomCss = ".x{top:0;}",
      BrandingPresetDarkIndicators = true
    };
    config.BrandingDrawerLinks.Add(new DrawerLink { Name = "Blog", Url = "https://x/blog", Icon = "rss_feed", NewTab = true });

    var dto = GetBranding(config);

    Assert.True(dto.Enabled);
    Assert.Equal("https://x/logo.png", dto.LogoUrl);
    Assert.Equal("#ff0000", dto.AccentColor);
    Assert.Equal("Inter", dto.FontFamily);
    Assert.Equal(".x{top:0;}", dto.CustomCss);
    Assert.True(dto.PresetDarkIndicators);
    var link = Assert.Single(dto.DrawerLinks);
    Assert.Equal("Blog", link.Name);
    Assert.Equal("https://x/blog", link.Url);
    Assert.True(link.NewTab);
  }
}
