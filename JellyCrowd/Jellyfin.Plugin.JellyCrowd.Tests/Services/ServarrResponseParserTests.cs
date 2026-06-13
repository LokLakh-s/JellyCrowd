using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrResponseParser"/>.
/// </summary>
public class ServarrResponseParserTests
{
  [Fact]
  public void ParseResources_RootFolders_UsesPathLabel()
  {
    var json = "[{\"id\":1,\"path\":\"/movies\"},{\"id\":2,\"path\":\"/movies4k\"}]";

    var result = ServarrResponseParser.ParseResources(json, "path");

    Assert.Equal(2, result.Count);
    Assert.Equal(1, result[0].Id);
    Assert.Equal("/movies", result[0].Name);
    Assert.Equal("/movies4k", result[1].Name);
  }

  [Fact]
  public void ParseResources_QualityProfiles_UsesNameLabel()
  {
    var json = "[{\"id\":4,\"name\":\"HD-1080p\"},{\"id\":5,\"name\":\"Ultra-HD\"}]";

    var result = ServarrResponseParser.ParseResources(json, "name");

    Assert.Equal(2, result.Count);
    Assert.Equal(4, result[0].Id);
    Assert.Equal("HD-1080p", result[0].Name);
  }

  [Fact]
  public void ParseResources_SkipsEntriesWithoutId_AndFallsBackToIdWhenLabelMissing()
  {
    var json = "[{\"path\":\"/no-id\"},{\"id\":7}]";

    var result = ServarrResponseParser.ParseResources(json, "path");

    Assert.Single(result);
    Assert.Equal(7, result[0].Id);
    Assert.Equal("7", result[0].Name);
  }

  [Fact]
  public void ParseResources_NonArray_ReturnsEmpty()
  {
    Assert.Empty(ServarrResponseParser.ParseResources("{\"message\":\"unauthorized\"}", "name"));
  }
}
