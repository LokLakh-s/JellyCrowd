using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="WebInjection"/> (the request-time index.html script injection).
/// </summary>
public class WebInjectionTests
{
  [Fact]
  public void InjectScript_InsertsTagBeforeFinalBodyClose()
  {
    var result = WebInjection.InjectScript("<html><body>hi</body></html>");

    Assert.Equal("<html><body>hi" + WebInjection.ScriptTag + "</body></html>", result);
  }

  [Fact]
  public void InjectScript_IsIdempotent()
  {
    var once = WebInjection.InjectScript("<html><body></body></html>");
    var twice = WebInjection.InjectScript(once);

    Assert.Equal(once, twice); // the tag is added exactly once
  }

  [Theory]
  [InlineData("")]
  [InlineData("<html><head></head></html>")] // no </body>
  public void InjectScript_NoBody_ReturnsInputUnchanged(string html)
  {
    Assert.Equal(html, WebInjection.InjectScript(html));
  }

  [Fact]
  public void InjectScript_UsesTheLastBodyClose()
  {
    // Defensive: insert before the final </body>, not a stray earlier one.
    var result = WebInjection.InjectScript("<body>a</body><div>b</body>");

    Assert.EndsWith(WebInjection.ScriptTag + "</body>", result);
  }
}
