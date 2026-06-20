using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ServarrIndexerParser"/>.
/// </summary>
public class ServarrIndexerParserTests
{
  [Fact]
  public void CountEnabled_CountsOnlyEnabled()
  {
    const string Json = "[{\"name\":\"a\",\"enable\":true},{\"name\":\"b\",\"enable\":false},{\"name\":\"c\",\"enable\":true}]";
    Assert.Equal(2, ServarrIndexerParser.CountEnabled(Json));
  }

  [Fact]
  public void CountEnabled_EmptyOrInvalid_ReturnsZero()
  {
    Assert.Equal(0, ServarrIndexerParser.CountEnabled("[]"));
    Assert.Equal(0, ServarrIndexerParser.CountEnabled(null));
    Assert.Equal(0, ServarrIndexerParser.CountEnabled("not json"));
    Assert.Equal(0, ServarrIndexerParser.CountEnabled("{\"enable\":true}"));
  }
}
