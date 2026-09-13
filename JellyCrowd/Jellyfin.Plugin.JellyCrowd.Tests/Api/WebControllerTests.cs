using Jellyfin.Plugin.JellyCrowd.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="WebController"/> embedded-asset serving.
/// </summary>
public class WebControllerTests
{
  private static WebController Create()
    => new()
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

  [Theory]
  [InlineData("catalog.html", "text/html; charset=utf-8")]
  [InlineData("catalog.js", "text/javascript; charset=utf-8")]
  [InlineData("jellycrowd.css", "text/css; charset=utf-8")]
  [InlineData("strings/en.json", "application/json; charset=utf-8")]
  [InlineData("strings/fr.json", "application/json; charset=utf-8")]
  public void GetAsset_KnownAsset_ReturnsFileWithContentType(string path, string expectedContentType)
  {
    var controller = Create();

    var result = controller.GetAsset(path);

    var file = Assert.IsType<FileStreamResult>(result);
    Assert.Equal(expectedContentType, file.ContentType);
  }

  [Fact]
  public void GetAsset_UnknownAsset_ReturnsNotFound()
  {
    var controller = Create();

    Assert.IsType<NotFoundResult>(controller.GetAsset("does-not-exist.html"));
  }

  [Fact]
  public void GetAsset_CarriesACacheValidator_SoAPluginUpdateIsNotServedStale()
  {
    // A cached asset with no validator is what let a browser run new scripts against an old translation
    // catalog, showing raw keys until a hard refresh.
    var controller = Create();

    var file = Assert.IsType<FileStreamResult>(controller.GetAsset("strings/fr.json"));

    Assert.NotNull(file.EntityTag);
    Assert.Equal("no-cache", controller.Response.Headers.CacheControl);
  }

  [Fact]
  public void AssetETag_ChangesWithThePluginVersion_AndWithTheAsset()
  {
    Assert.NotEqual(WebController.AssetETag("1.0.0.0", "catalog.js"), WebController.AssetETag("1.0.1.0", "catalog.js"));
    Assert.NotEqual(WebController.AssetETag("1.0.0.0", "catalog.js"), WebController.AssetETag("1.0.0.0", "strings/fr.json"));
    Assert.Equal(WebController.AssetETag("1.0.0.0", "catalog.js"), WebController.AssetETag("1.0.0.0", "catalog.js"));
  }

  [Fact]
  public void AssetETag_IsAQuotedEntityTag()
  {
    var etag = WebController.AssetETag("1.2.3.4", "strings/fr.json");

    Assert.StartsWith("\"", etag, System.StringComparison.Ordinal);
    Assert.EndsWith("\"", etag, System.StringComparison.Ordinal);
    Assert.DoesNotContain("\"", etag[1..^1], System.StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("../secret")]
  [InlineData("a/../../b")]
  [InlineData("bad name.html")]
  [InlineData("")]
  public void GetAsset_UnsafeOrEmptyPath_ReturnsNotFound(string path)
  {
    var controller = Create();

    Assert.IsType<NotFoundResult>(controller.GetAsset(path));
  }
}
