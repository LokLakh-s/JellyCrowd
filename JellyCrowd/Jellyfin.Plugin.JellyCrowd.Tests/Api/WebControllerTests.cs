using Jellyfin.Plugin.JellyCrowd.Api;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="WebController"/> embedded-asset serving.
/// </summary>
public class WebControllerTests
{
  [Theory]
  [InlineData("catalog.html", "text/html; charset=utf-8")]
  [InlineData("catalog.js", "text/javascript; charset=utf-8")]
  [InlineData("jellycrowd.css", "text/css; charset=utf-8")]
  [InlineData("strings/en.json", "application/json; charset=utf-8")]
  [InlineData("strings/fr.json", "application/json; charset=utf-8")]
  public void GetAsset_KnownAsset_ReturnsFileWithContentType(string path, string expectedContentType)
  {
    var controller = new WebController();

    var result = controller.GetAsset(path);

    var file = Assert.IsType<FileStreamResult>(result);
    Assert.Equal(expectedContentType, file.ContentType);
  }

  [Fact]
  public void GetAsset_UnknownAsset_ReturnsNotFound()
  {
    var controller = new WebController();

    Assert.IsType<NotFoundResult>(controller.GetAsset("does-not-exist.html"));
  }

  [Theory]
  [InlineData("../secret")]
  [InlineData("a/../../b")]
  [InlineData("bad name.html")]
  [InlineData("")]
  public void GetAsset_UnsafeOrEmptyPath_ReturnsNotFound(string path)
  {
    var controller = new WebController();

    Assert.IsType<NotFoundResult>(controller.GetAsset(path));
  }
}
