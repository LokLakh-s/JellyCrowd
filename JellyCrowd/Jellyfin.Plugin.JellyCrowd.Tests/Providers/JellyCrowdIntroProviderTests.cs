using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Providers;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Providers;

/// <summary>
/// Tests for <see cref="JellyCrowdIntroProvider"/>, focused on the web/desktop-only client gate that keeps
/// local intros away from native apps and mobile browsers (which can fail to start playback when a pre-roll
/// is prepended). The gate short-circuits before the pre-roll registry is queried, so verifying whether the
/// registry is reached distinguishes "gate blocked" from "gate passed".
/// </summary>
public class JellyCrowdIntroProviderTests
{
  private const string DesktopUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120 Safari/537.36";
  private const string AndroidUserAgent = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36";
  private const string IPhoneUserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605 Version/17 Mobile/15E148 Safari/604";

  private static PluginConfiguration WebOnlyConfig() => new()
  {
    LocalIntrosEnabled = true,
    LocalIntrosOnMovies = true,
    LocalIntrosWebOnly = true,
    LocalIntrosFolderName = "intros"
  };

  private static (IHttpContextAccessor Http, IAuthorizationContext Auth) ClientContext(string client, string userAgent = DesktopUserAgent)
  {
    var ctx = new DefaultHttpContext();
    ctx.Request.Headers.UserAgent = userAgent;
    var http = new Mock<IHttpContextAccessor>();
    http.Setup(h => h.HttpContext).Returns(ctx);
    var auth = new Mock<IAuthorizationContext>();
    auth.Setup(a => a.GetAuthorizationInfo(It.IsAny<HttpRequest>()))
      .ReturnsAsync(new AuthorizationInfo { Client = client });
    return (http.Object, auth.Object);
  }

  // A registry that yields one pre-roll id, so a gate that passes produces an intro.
  private static Mock<IIntroFileRegistry> Registry()
  {
    var registry = new Mock<IIntroFileRegistry>();
    registry.Setup(r => r.EnsureAndGetIds(It.IsAny<ILibraryManager>(), It.IsAny<string>()))
      .Returns(new[] { Guid.NewGuid() });
    return registry;
  }

  private static JellyCrowdIntroProvider Provider(Mock<IIntroFileRegistry> registry, IHttpContextAccessor? http, IAuthorizationContext? auth, PluginConfiguration cfg)
    => new(new Mock<ILibraryManager>().Object, () => cfg, registry.Object, http, auth);

  private static void VerifyRegistry(Mock<IIntroFileRegistry> registry, Times times)
    => registry.Verify(r => r.EnsureAndGetIds(It.IsAny<ILibraryManager>(), It.IsAny<string>()), times);

  [Fact]
  public async Task GetIntros_NativeClient_ReturnsNoneWithoutQueryingTheRegistry()
  {
    var registry = Registry();
    var (http, auth) = ClientContext("Jellyfin iOS");

    var result = await Provider(registry, http, auth, WebOnlyConfig()).GetIntros(new Movie(), null!);

    Assert.Empty(result);
    VerifyRegistry(registry, Times.Never());
  }

  [Fact]
  public async Task GetIntros_WebClient_PassesGate()
  {
    var registry = Registry();
    var (http, auth) = ClientContext("Jellyfin Web");

    var result = await Provider(registry, http, auth, WebOnlyConfig()).GetIntros(new Movie(), null!);

    Assert.NotEmpty(result);
    VerifyRegistry(registry, Times.Once());
  }

  [Fact]
  public async Task GetIntros_DesktopMediaPlayer_PassesGate()
  {
    var registry = Registry();
    var (http, auth) = ClientContext("Jellyfin Media Player");

    var result = await Provider(registry, http, auth, WebOnlyConfig()).GetIntros(new Movie(), null!);

    Assert.NotEmpty(result);
    VerifyRegistry(registry, Times.Once());
  }

  [Fact]
  public async Task GetIntros_RestrictionOff_AllowsNativeClient()
  {
    var registry = Registry();
    var (http, auth) = ClientContext("Jellyfin iOS");
    var cfg = WebOnlyConfig();
    cfg.LocalIntrosWebOnly = false;

    var result = await Provider(registry, http, auth, cfg).GetIntros(new Movie(), null!);

    Assert.NotEmpty(result);
    VerifyRegistry(registry, Times.Once());
  }

  [Fact]
  public async Task GetIntros_NoRequestContext_FailsClosed()
  {
    // Web-only ON but no accessor → the client can't be identified → fail CLOSED: don't risk a broken
    // pre-roll on a native app. The gate short-circuits, so the registry is never queried.
    var registry = Registry();

    var result = await Provider(registry, null, null, WebOnlyConfig()).GetIntros(new Movie(), null!);

    Assert.Empty(result);
    VerifyRegistry(registry, Times.Never());
  }

  [Fact]
  public async Task GetIntros_MobileWebBrowser_IsBlocked()
  {
    // A phone browser reports the "Jellyfin Web" client but chokes on a prepended pre-roll — tell it apart
    // from a desktop browser by the mobile User-Agent and block it (the reported Android case).
    var registry = Registry();
    var (http, auth) = ClientContext("Jellyfin Web", AndroidUserAgent);

    var result = await Provider(registry, http, auth, WebOnlyConfig()).GetIntros(new Movie(), null!);

    Assert.Empty(result);
    VerifyRegistry(registry, Times.Never());
  }

  [Fact]
  public async Task GetIntros_IPhoneWebBrowser_IsBlocked()
  {
    var registry = Registry();
    var (http, auth) = ClientContext("Jellyfin Web", IPhoneUserAgent);

    var result = await Provider(registry, http, auth, WebOnlyConfig()).GetIntros(new Movie(), null!);

    Assert.Empty(result);
    VerifyRegistry(registry, Times.Never());
  }

  [Fact]
  public async Task GetIntros_NativeClientContainingWeb_IsBlocked()
  {
    // "Jellyfin webOS" (LG TV) contains "web" but is a native client — precise matching must block it.
    var registry = Registry();
    var (http, auth) = ClientContext("Jellyfin webOS");

    var result = await Provider(registry, http, auth, WebOnlyConfig()).GetIntros(new Movie(), null!);

    Assert.Empty(result);
    VerifyRegistry(registry, Times.Never());
  }
}
