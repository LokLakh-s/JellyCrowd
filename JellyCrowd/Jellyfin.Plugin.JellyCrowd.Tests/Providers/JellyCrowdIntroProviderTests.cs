using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Providers;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Providers;

/// <summary>
/// Tests for <see cref="JellyCrowdIntroProvider"/>, focused on the web/desktop-only client gate that keeps
/// local intros away from native apps (which can fail to start playback when a pre-roll is prepended).
/// The gate short-circuits before <see cref="ILibraryManager.GetVirtualFolders"/>, so verifying whether that
/// call is reached distinguishes "gate blocked" from "gate passed".
/// </summary>
public class JellyCrowdIntroProviderTests
{
  private static PluginConfiguration WebOnlyConfig() => new()
  {
    LocalIntrosEnabled = true,
    LocalIntrosOnMovies = true,
    LocalIntrosWebOnly = true,
    LocalIntrosFolderName = "intros"
  };

  private const string DesktopUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120 Safari/537.36";
  private const string AndroidUserAgent = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36";
  private const string IPhoneUserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605 Version/17 Mobile/15E148 Safari/604";

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

  [Fact]
  public async Task GetIntros_NativeClient_ReturnsNoneWithoutTouchingLibrary()
  {
    var library = new Mock<ILibraryManager>();
    var (http, auth) = ClientContext("Jellyfin iOS");
    var cfg = WebOnlyConfig();
    var provider = new JellyCrowdIntroProvider(library.Object, () => cfg, http, auth);

    var result = await provider.GetIntros(new Movie(), null!);

    Assert.Empty(result);
    library.Verify(l => l.GetVirtualFolders(), Times.Never);
  }

  [Fact]
  public async Task GetIntros_WebClient_PassesGate()
  {
    var library = new Mock<ILibraryManager>();
    library.Setup(l => l.GetVirtualFolders()).Returns(new List<VirtualFolderInfo>());
    var (http, auth) = ClientContext("Jellyfin Web");
    var cfg = WebOnlyConfig();
    var provider = new JellyCrowdIntroProvider(library.Object, () => cfg, http, auth);

    await provider.GetIntros(new Movie(), null!);

    library.Verify(l => l.GetVirtualFolders(), Times.AtLeastOnce);
  }

  [Fact]
  public async Task GetIntros_DesktopMediaPlayer_PassesGate()
  {
    var library = new Mock<ILibraryManager>();
    library.Setup(l => l.GetVirtualFolders()).Returns(new List<VirtualFolderInfo>());
    var (http, auth) = ClientContext("Jellyfin Media Player");
    var cfg = WebOnlyConfig();
    var provider = new JellyCrowdIntroProvider(library.Object, () => cfg, http, auth);

    await provider.GetIntros(new Movie(), null!);

    library.Verify(l => l.GetVirtualFolders(), Times.AtLeastOnce);
  }

  [Fact]
  public async Task GetIntros_RestrictionOff_AllowsNativeClient()
  {
    var library = new Mock<ILibraryManager>();
    library.Setup(l => l.GetVirtualFolders()).Returns(new List<VirtualFolderInfo>());
    var (http, auth) = ClientContext("Jellyfin iOS");
    var cfg = WebOnlyConfig();
    cfg.LocalIntrosWebOnly = false;
    var provider = new JellyCrowdIntroProvider(library.Object, () => cfg, http, auth);

    await provider.GetIntros(new Movie(), null!);

    library.Verify(l => l.GetVirtualFolders(), Times.AtLeastOnce);
  }

  [Fact]
  public async Task GetIntros_NoRequestContext_FailsClosed()
  {
    // Web-only ON but no accessor → the client can't be identified → fail CLOSED: don't risk a broken
    // pre-roll on a native app. The gate short-circuits, so the library is never queried.
    var library = new Mock<ILibraryManager>();
    var cfg = WebOnlyConfig();
    var provider = new JellyCrowdIntroProvider(library.Object, () => cfg, httpContextAccessor: null, authorizationContext: null);

    var result = await provider.GetIntros(new Movie(), null!);

    Assert.Empty(result);
    library.Verify(l => l.GetVirtualFolders(), Times.Never);
  }

  [Fact]
  public async Task GetIntros_MobileWebBrowser_IsBlocked()
  {
    // A phone browser reports the "Jellyfin Web" client but chokes on a prepended pre-roll — tell it apart
    // from a desktop browser by the mobile User-Agent and block it (the reported Android case).
    var library = new Mock<ILibraryManager>();
    var (http, auth) = ClientContext("Jellyfin Web", AndroidUserAgent);
    var cfg = WebOnlyConfig();
    var provider = new JellyCrowdIntroProvider(library.Object, () => cfg, http, auth);

    var result = await provider.GetIntros(new Movie(), null!);

    Assert.Empty(result);
    library.Verify(l => l.GetVirtualFolders(), Times.Never);
  }

  [Fact]
  public async Task GetIntros_IPhoneWebBrowser_IsBlocked()
  {
    var library = new Mock<ILibraryManager>();
    var (http, auth) = ClientContext("Jellyfin Web", IPhoneUserAgent);
    var cfg = WebOnlyConfig();
    var provider = new JellyCrowdIntroProvider(library.Object, () => cfg, http, auth);

    var result = await provider.GetIntros(new Movie(), null!);

    Assert.Empty(result);
    library.Verify(l => l.GetVirtualFolders(), Times.Never);
  }

  [Fact]
  public async Task GetIntros_NativeClientContainingWeb_IsBlocked()
  {
    // "Jellyfin webOS" (LG TV) contains "web" but is a native client — precise matching must block it.
    var library = new Mock<ILibraryManager>();
    var (http, auth) = ClientContext("Jellyfin webOS");
    var cfg = WebOnlyConfig();
    var provider = new JellyCrowdIntroProvider(library.Object, () => cfg, http, auth);

    var result = await provider.GetIntros(new Movie(), null!);

    Assert.Empty(result);
    library.Verify(l => l.GetVirtualFolders(), Times.Never);
  }
}
