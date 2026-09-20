using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    // The plugin is public: its guide describes Jelly Crowd, not one server. A host name is how an
    // instance's identity leaks into it, so the shipped text carries none — whoever wants their own name
    // in there supplies their own content file. Matching the shape rather than one server's name keeps
    // this test from naming anybody either.
    var result = Assert.IsType<FileStreamResult>(Create().GetContent());

    using var reader = new StreamReader(result.FileStream);
    var host = Regex.Match(reader.ReadToEnd(), "\\b[a-z0-9][a-z0-9-]*\\.(tv|fr|com|net|org|io)\\b", RegexOptions.IgnoreCase);

    Assert.False(host.Success, "the shipped guide names a host: " + host.Value);
  }

  [Fact]
  public void GetContent_ShippedScreenshotsAreAllThere()
  {
    // The guide may ship screenshots, but only ones taken on the dev stack (see dev-stack/). What this
    // guards is the wiring: a step naming a figure the plugin does not carry would render a gap where
    // the guide promises a picture.
    var result = Assert.IsType<FileStreamResult>(Create().GetContent());

    using var reader = new StreamReader(result.FileStream);
    using var document = JsonDocument.Parse(reader.ReadToEnd());
    var images = document.RootElement.GetProperty("images");

    foreach (var language in document.RootElement.GetProperty("languages").EnumerateObject())
    {
      foreach (var step in language.Value.GetProperty("steps").EnumerateArray())
      {
        foreach (var fig in step.GetProperty("figs").EnumerateArray())
        {
          var key = fig[0].GetString()!;
          Assert.True(images.TryGetProperty(key, out var file), $"{language.Name}: step figure '{key}' has no image");
          Assert.IsType<FileStreamResult>(Create().GetImage(file.GetString()));
        }
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
