using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.JellyCrowd.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="GuideController"/>. Without a running plugin there is no data folder, so these
/// exercise what every install gets: the guide the plugin itself ships. Which file wins when an instance
/// supplies its own is covered by <c>GuideAssetsTests</c>.
/// </summary>
public class GuideControllerTests
{
  private static GuideController Create()
    => new() { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

  [Fact]
  public void GetContent_ServesThePluginsOwnGuide()
  {
    var result = Assert.IsType<FileStreamResult>(Create().GetContent());

    Assert.Equal("application/json; charset=utf-8", result.ContentType);
  }

  [Fact]
  public void GetContent_IsAUsableGuideDocument()
  {
    var result = Assert.IsType<FileStreamResult>(Create().GetContent());

    using var reader = new StreamReader(result.FileStream);
    using var document = JsonDocument.Parse(reader.ReadToEnd());
    var languages = document.RootElement.GetProperty("languages");
    Assert.True(languages.TryGetProperty("en", out var english));
    Assert.False(string.IsNullOrWhiteSpace(english.GetProperty("h1").GetString()));
    Assert.True(english.GetProperty("steps").GetArrayLength() > 0);
  }

  [Fact]
  public void GetContent_ShippedGuideNamesNoInstance()
  {
    // The plugin is public: its guide must describe Jelly Crowd, not one server. An instance that wants
    // its own name in there supplies its own content file.
    var result = Assert.IsType<FileStreamResult>(Create().GetContent());

    using var reader = new StreamReader(result.FileStream);
    var json = reader.ReadToEnd();
    Assert.DoesNotContain("Keeklah", json, System.StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void GetContent_ShippedGuideCarriesNobodysScreenshots()
  {
    var result = Assert.IsType<FileStreamResult>(Create().GetContent());

    using var reader = new StreamReader(result.FileStream);
    using var document = JsonDocument.Parse(reader.ReadToEnd());

    // No images of any real library ship with the plugin, and no step claims one.
    Assert.Empty(document.RootElement.GetProperty("images").EnumerateObject());
    foreach (var language in document.RootElement.GetProperty("languages").EnumerateObject())
    {
      foreach (var step in language.Value.GetProperty("steps").EnumerateArray())
      {
        Assert.Equal(0, step.GetProperty("figs").GetArrayLength());
      }
    }
  }

  [Theory]
  [InlineData("../../../system.xml")]
  [InlineData("guide-content.json")]
  [InlineData("nope.jpg")]
  public void GetImage_UnknownOrUnsafe_NotFound(string name)
    => Assert.IsType<NotFoundResult>(Create().GetImage(name));
}
