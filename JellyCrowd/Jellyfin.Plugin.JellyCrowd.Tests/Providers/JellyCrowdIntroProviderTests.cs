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

  private static (IHttpContextAccessor Http, IAuthorizationContext Auth) ClientContext(string client)
  {
    var http = new Mock<IHttpContextAccessor>();
    http.Setup(h => h.HttpContext).Returns(new DefaultHttpContext());
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
  public async Task GetIntros_NoRequestContext_FailsOpen()
  {
    // Web-only ON but no accessor → the client can't be identified → fail open (never block playback).
    var library = new Mock<ILibraryManager>();
    library.Setup(l => l.GetVirtualFolders()).Returns(new List<VirtualFolderInfo>());
    var cfg = WebOnlyConfig();
    var provider = new JellyCrowdIntroProvider(library.Object, () => cfg, httpContextAccessor: null, authorizationContext: null);

    await provider.GetIntros(new Movie(), null!);

    library.Verify(l => l.GetVirtualFolders(), Times.AtLeastOnce);
  }
}
