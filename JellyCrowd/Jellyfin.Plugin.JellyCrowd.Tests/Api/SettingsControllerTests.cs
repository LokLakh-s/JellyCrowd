using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="SettingsController"/>.
/// </summary>
public class SettingsControllerTests
{
  private static LanguageSettingDto GetLanguage(string? configured)
  {
    var controller = new SettingsController(() => new PluginConfiguration { Language = configured! });
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
}
