using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="AutoRequestsController"/>.
/// </summary>
public class AutoRequestsControllerTests
{
  private static readonly Guid Viewer = Guid.NewGuid();

  private static (AutoRequestsController Controller, UserNotificationPrefs Stored) Create(PluginConfiguration config, UserNotificationPrefs? stored = null)
  {
    var prefs = stored ?? new UserNotificationPrefs { UserId = Viewer };
    var store = new Mock<IUserPrefsStore>();
    store.Setup(s => s.GetAsync(Viewer, It.IsAny<CancellationToken>())).ReturnsAsync(() => prefs);
    store.Setup(s => s.SetAsync(It.IsAny<UserNotificationPrefs>(), It.IsAny<CancellationToken>()))
      .Callback<UserNotificationPrefs, CancellationToken>((p, _) => prefs = p)
      .ReturnsAsync((UserNotificationPrefs p, CancellationToken _) => p);
    var accessor = Mock.Of<ICurrentUserAccessor>(a => a.GetUserIdAsync(It.IsAny<HttpRequest>()) == Task.FromResult(Viewer));
    var controller = new AutoRequestsController(store.Object, accessor, () => config)
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
    return (controller, prefs);
  }

  [Fact]
  public async Task Mine_ReportsOfferOptInAndThreshold()
  {
    var config = new PluginConfiguration { AutoNextSeasonEnabled = true, AutoNextSeasonEpisodesLeft = 3 };
    var (controller, _) = Create(config, new UserNotificationPrefs { UserId = Viewer, AutoRequestNextSeason = true });

    var dto = Assert.IsType<AutoNextSeasonDto>(Assert.IsType<OkObjectResult>((await controller.Mine(CancellationToken.None)).Result).Value);

    Assert.True(dto.Available);
    Assert.True(dto.Enabled);
    Assert.Equal(3, dto.EpisodesLeft);
  }

  [Fact]
  public async Task SetMine_On_KeepsTheNotificationPreferences()
  {
    var stored = new UserNotificationPrefs { UserId = Viewer, Email = "me@example.org", NotifyDecisions = true };
    var store = new Mock<IUserPrefsStore>();
    store.Setup(s => s.GetAsync(Viewer, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
    UserNotificationPrefs? saved = null;
    store.Setup(s => s.SetAsync(It.IsAny<UserNotificationPrefs>(), It.IsAny<CancellationToken>()))
      .Callback<UserNotificationPrefs, CancellationToken>((p, _) => saved = p)
      .ReturnsAsync((UserNotificationPrefs p, CancellationToken _) => p);
    var accessor = Mock.Of<ICurrentUserAccessor>(a => a.GetUserIdAsync(It.IsAny<HttpRequest>()) == Task.FromResult(Viewer));
    var controller = new AutoRequestsController(store.Object, accessor, () => new PluginConfiguration { AutoNextSeasonEnabled = true })
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    var result = await controller.SetMine(true, CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
    Assert.NotNull(saved);
    Assert.True(saved!.AutoRequestNextSeason);
    Assert.Equal("me@example.org", saved.Email);
    Assert.True(saved.NotifyDecisions);
  }

  [Fact]
  public async Task SetMine_On_WhenNotOffered_Returns403()
  {
    var (controller, _) = Create(new PluginConfiguration { AutoNextSeasonEnabled = false });

    var result = await controller.SetMine(true, CancellationToken.None);

    Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
  }

  [Fact]
  public async Task SetMine_Off_IsAlwaysAllowed()
  {
    var (controller, _) = Create(new PluginConfiguration { AutoNextSeasonEnabled = false });

    Assert.IsType<NoContentResult>(await controller.SetMine(false, CancellationToken.None));
  }
}
