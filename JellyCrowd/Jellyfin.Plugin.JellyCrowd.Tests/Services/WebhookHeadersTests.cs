using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="WebhookHeaders"/>.
/// </summary>
public class WebhookHeadersTests
{
  [Fact]
  public void Parse_ReturnsEmpty_ForNullOrBlank()
  {
    Assert.Empty(WebhookHeaders.Parse(null));
    Assert.Empty(WebhookHeaders.Parse("   "));
  }

  [Fact]
  public void Parse_SplitsNameAndValueOnFirstColon_AndTrims()
  {
    var headers = WebhookHeaders.Parse("Authorization: Bearer abc:123\n X-Source :  jelly ");

    Assert.Equal(2, headers.Count);
    Assert.Equal("Authorization", headers[0].Key);
    Assert.Equal("Bearer abc:123", headers[0].Value);
    Assert.Equal("X-Source", headers[1].Key);
    Assert.Equal("jelly", headers[1].Value);
  }

  [Fact]
  public void Parse_SkipsBlankLinesAndLinesWithoutColonOrName()
  {
    var headers = WebhookHeaders.Parse("\nno-colon-here\n: novalue\nX-Ok: 1\r\n");

    Assert.Single(headers);
    Assert.Equal("X-Ok", headers[0].Key);
    Assert.Equal("1", headers[0].Value);
  }
}
