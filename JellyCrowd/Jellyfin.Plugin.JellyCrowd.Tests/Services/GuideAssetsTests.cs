using System;
using System.IO;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="GuideAssets"/>: which guide an instance gets, and what a request is allowed to
/// ask for by name.
/// </summary>
public sealed class GuideAssetsTests : IDisposable
{
  private readonly string _dataFolder = Path.Combine(Path.GetTempPath(), "jc-guide-" + Guid.NewGuid());

  public void Dispose()
  {
    if (Directory.Exists(_dataFolder))
    {
      Directory.Delete(_dataFolder, recursive: true);
    }
  }

  private string WriteOverride(string relative, string content = "{}")
  {
    var path = Path.Combine(_dataFolder, GuideAssets.OverrideFolder, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    return path;
  }

  [Fact]
  public void CustomContentPath_NoOverride_FallsBackToThePluginsOwnGuide()
  {
    // Nothing in the data folder: the caller serves the embedded (public, nameless) content.
    Directory.CreateDirectory(_dataFolder);

    Assert.Null(GuideAssets.CustomContentPath(_dataFolder));
  }

  [Fact]
  public void CustomContentPath_OverridePresent_WinsOverThePluginsOwn()
  {
    var expected = WriteOverride(GuideAssets.ContentFileName, "{\"languages\":{}}");

    Assert.Equal(expected, GuideAssets.CustomContentPath(_dataFolder));
  }

  [Fact]
  public void CustomContentPath_NoDataFolderYet_IsNotAFailure()
  {
    // Early start-up, or a unit test: no plugin instance, so no override — never an exception.
    Assert.Null(GuideAssets.CustomContentPath(null));
    Assert.Null(GuideAssets.CustomContentPath(string.Empty));
  }

  [Fact]
  public void CustomImagePath_OverridePresent_IsUsed()
  {
    var expected = WriteOverride(Path.Combine("img", "guide-catalog.jpg"), "jpeg bytes");

    Assert.Equal(expected, GuideAssets.CustomImagePath(_dataFolder, "guide-catalog.jpg"));
  }

  [Fact]
  public void CustomImagePath_MissingImage_FallsBack()
  {
    Directory.CreateDirectory(Path.Combine(_dataFolder, GuideAssets.OverrideFolder, "img"));

    Assert.Null(GuideAssets.CustomImagePath(_dataFolder, "guide-catalog.jpg"));
  }

  [Theory]
  [InlineData("guide-catalog.jpg")]
  [InlineData("Guide_Catalog.PNG")]
  [InlineData("a.webp")]
  public void IsSafeImageName_AcceptsPlainImageNames(string name)
    => Assert.True(GuideAssets.IsSafeImageName(name));

  [Theory]
  [InlineData("../../config/system.xml")]   // walking out of the folder
  [InlineData("img/guide-catalog.jpg")]     // a path, not a name
  [InlineData("guide-catalog.jpg.exe")]     // not an image
  [InlineData("guide-catalog")]             // no extension
  [InlineData(".hidden.png")]               // must start with a letter or digit
  [InlineData("")]
  [InlineData(null)]
  public void IsSafeImageName_RefusesAnythingElse(string? name)
    => Assert.False(GuideAssets.IsSafeImageName(name));

  [Fact]
  public void CustomImagePath_UnsafeName_NeverTouchesTheDisk()
  {
    // Even if a file with that shape existed, the name is refused before any path is built.
    WriteOverride(GuideAssets.ContentFileName);

    Assert.Null(GuideAssets.CustomImagePath(_dataFolder, "../" + GuideAssets.ContentFileName));
  }

  [Theory]
  [InlineData("a.jpg", "image/jpeg")]
  [InlineData("a.jpeg", "image/jpeg")]
  [InlineData("a.png", "image/png")]
  [InlineData("a.webp", "image/webp")]
  [InlineData("a.svg", "image/svg+xml")]
  public void ImageContentType_IsTheRealType(string name, string expected)
    => Assert.Equal(expected, GuideAssets.ImageContentType(name));
}
