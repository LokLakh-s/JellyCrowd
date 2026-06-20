using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrIndexerParser"/>.
/// </summary>
public class ServarrIndexerParserTests
{
  [Fact]
  public void CountEnabled_CountsIndexersWithAnySearchFlag()
  {
    // Radarr/Sonarr indexers expose enableRss / enableAutomaticSearch / enableInteractiveSearch.
    const string Json = "["
      + "{\"name\":\"a\",\"enableRss\":true,\"enableAutomaticSearch\":true,\"enableInteractiveSearch\":true},"
      + "{\"name\":\"b\",\"enableRss\":false,\"enableAutomaticSearch\":false,\"enableInteractiveSearch\":false},"
      + "{\"name\":\"c\",\"enableRss\":false,\"enableAutomaticSearch\":true,\"enableInteractiveSearch\":false}"
      + "]";
    Assert.Equal(2, ServarrIndexerParser.CountEnabled(Json));
  }

  [Fact]
  public void CountEnabled_InteractiveOnly_Counts()
  {
    const string Json = "[{\"name\":\"a\",\"enableRss\":false,\"enableAutomaticSearch\":false,\"enableInteractiveSearch\":true}]";
    Assert.Equal(1, ServarrIndexerParser.CountEnabled(Json));
  }

  [Fact]
  public void CountEnabled_EmptyOrInvalid_ReturnsZero()
  {
    Assert.Equal(0, ServarrIndexerParser.CountEnabled("[]"));
    Assert.Equal(0, ServarrIndexerParser.CountEnabled(null));
    Assert.Equal(0, ServarrIndexerParser.CountEnabled("not json"));
    Assert.Equal(0, ServarrIndexerParser.CountEnabled("{\"enableRss\":true}"));
    Assert.Equal(0, ServarrIndexerParser.CountEnabled("[{\"name\":\"a\",\"enableRss\":false}]"));
  }
}
