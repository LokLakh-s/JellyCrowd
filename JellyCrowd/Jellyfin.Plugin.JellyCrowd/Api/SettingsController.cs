using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Exposes non-sensitive plugin settings the user-facing pages need to render, such as the
/// configured display language. Read-only; the admin manages these on the configuration page.
/// </summary>
[ApiController]
[Route("JellyCrowd/Settings")]
[Produces(MediaTypeNames.Application.Json)]
public class SettingsController : ControllerBase
{
  private readonly Func<PluginConfiguration> _config;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly ILibraryManager _libraryManager;
  private readonly IIntroFileRegistry _introRegistry;

  /// <summary>
  /// Initializes a new instance of the <see cref="SettingsController"/> class.
  /// </summary>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="userAccessor">The current-user accessor (to resolve administrator status).</param>
  /// <param name="libraryManager">The library manager.</param>
  /// <param name="introRegistry">Resolves the local-intro pre-roll item ids.</param>
  public SettingsController(Func<PluginConfiguration> config, ICurrentUserAccessor userAccessor, ILibraryManager libraryManager, IIntroFileRegistry introRegistry)
  {
    _config = config;
    _userAccessor = userAccessor;
    _libraryManager = libraryManager;
    _introRegistry = introRegistry;
  }

  /// <summary>
  /// Gets the configured UI/notification language (<c>"auto"</c> or a 2-letter code).
  /// </summary>
  /// <response code="200">The configured language.</response>
  /// <returns>The language setting.</returns>
  [HttpGet("Language")]
  [AllowAnonymous]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<LanguageSettingDto> GetLanguage()
  {
    var config = _config();
    return Ok(new LanguageSettingDto
    {
      Language = string.IsNullOrWhiteSpace(config.Language) ? "auto" : config.Language,
      Hidden = config.HiddenFromUsers,
      CommentsEnabled = config.CommentsEnabled,
      AllowUserRetrySearch = config.AllowUserRetrySearch,
      // Anonymous endpoint: only carry a *global* announcement here. A targeted announcement is delivered
      // per-user through /Settings/Visibility so its text never leaks to users outside its groups.
      AnnouncementText = config.AnnouncementGroupIds.Count == 0 ? (config.AnnouncementText ?? string.Empty) : string.Empty,
      AnnouncementLevel = string.IsNullOrWhiteSpace(config.AnnouncementLevel) ? "green" : config.AnnouncementLevel,
      // Only surface a link URL when the admin enabled it (a disabled, configured URL stays private).
      DiscordInviteUrl = config.DiscordInviteEnabled ? (config.DiscordInviteUrl ?? string.Empty) : string.Empty,
      SupportLinkUrl = config.SupportLinkEnabled ? (config.SupportLinkUrl ?? string.Empty) : string.Empty,
      GuideLinkUrl = config.GuideLinkEnabled ? (config.GuideLinkUrl ?? string.Empty) : string.Empty,
      SkipOutroEnabled = config.SkipOutroEnabled,
      HideNativeDrawer = config.HideNativeDrawerForNonAdmins
    });
  }

  /// <summary>
  /// Gets the Local Intros settings the web client needs: whether pre-rolls are on, whether they must be
  /// non-skippable, and the pre-roll item ids (so the injected script can recognise a pre-roll during
  /// playback). Anonymous and non-sensitive — item ids are opaque and the videos are already user-visible.
  /// </summary>
  /// <response code="200">The Local Intros settings.</response>
  /// <returns>The pre-roll enablement, non-skippable flag and item ids.</returns>
  [HttpGet("LocalIntros")]
  [AllowAnonymous]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<LocalIntrosSettingsDto> GetLocalIntros()
  {
    var config = _config();
    var itemIds = config.LocalIntrosEnabled
      ? _introRegistry.EnsureAndGetIds(_libraryManager, config.LocalIntrosFolderName)
        .Select(id => id.ToString("N", CultureInfo.InvariantCulture))
        .ToList()
      : new List<string>();

    return Ok(new LocalIntrosSettingsDto
    {
      Enabled = config.LocalIntrosEnabled,
      NonSkippable = config.LocalIntrosNonSkippable,
      ForceCinemaMode = config.LocalIntrosForceCinemaMode,
      ItemIds = itemIds,
    });
  }

  /// <summary>
  /// Gets the presentational branding settings the web client applies to theme the whole Jellyfin UI
  /// (logo, favicon, default avatar, background, accent colour, font, layout presets, custom CSS and
  /// custom drawer entries). Anonymous: all fields are cosmetic, and branding applies to every visitor.
  /// </summary>
  /// <response code="200">The branding settings.</response>
  /// <returns>The presentational branding configuration.</returns>
  [HttpGet("Branding")]
  [AllowAnonymous]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<BrandingDto> GetBranding()
  {
    var config = _config();
    return Ok(new BrandingDto
    {
      Enabled = config.BrandingEnabled,
      LogoUrl = config.BrandingLogoUrl ?? string.Empty,
      FaviconUrl = config.BrandingFaviconUrl ?? string.Empty,
      DefaultAvatarUrl = config.BrandingDefaultAvatarUrl ?? string.Empty,
      BackgroundUrl = config.BrandingBackgroundUrl ?? string.Empty,
      BackgroundColor = config.BrandingBackgroundColor ?? string.Empty,
      AccentColor = config.BrandingAccentColor ?? string.Empty,
      FontFamily = config.BrandingFontFamily ?? string.Empty,
      FontUrl = config.BrandingFontUrl ?? string.Empty,
      CustomCss = config.BrandingCustomCss ?? string.Empty,
      PresetCompactEpisodes = config.BrandingPresetCompactEpisodes,
      PresetDarkIndicators = config.BrandingPresetDarkIndicators,
      PresetNarrowChannels = config.BrandingPresetNarrowChannels,
      PresetHideBackdrop = config.BrandingPresetHideBackdrop,
      PresetButtonTweaks = config.BrandingPresetButtonTweaks,
      DrawerLinks = config.BrandingDrawerLinks
    });
  }

  /// <summary>
  /// Tells the caller whether the plugin should be shown to them: hidden ("config mode") only affects
  /// non-administrators. Authenticated so the current user's role is known.
  /// </summary>
  /// <response code="200">The visibility decision for the current user.</response>
  /// <returns>Whether the plugin is visible to the current user.</returns>
  [HttpGet("Visibility")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<VisibilitySettingDto>> GetVisibility()
  {
    var isAdmin = await _userAccessor.IsAdministratorAsync(Request).ConfigureAwait(false);
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var config = _config();
    var visible = Services.RequestPolicy.IsVisibleTo(config, userId, isAdmin);
    var showAnnouncement = Services.RequestPolicy.ShouldSeeAnnouncement(config, userId, isAdmin);
    return Ok(new VisibilitySettingDto
    {
      Visible = visible,
      IsAdmin = isAdmin,
      AnnouncementText = showAnnouncement ? (config.AnnouncementText ?? string.Empty) : string.Empty,
      AnnouncementLevel = string.IsNullOrWhiteSpace(config.AnnouncementLevel) ? "green" : config.AnnouncementLevel,
    });
  }

  /// <summary>
  /// Sets (or clears) the header announcement banner. Administrators only.
  /// </summary>
  /// <param name="dto">The announcement payload (empty text clears it).</param>
  /// <response code="204">The announcement was saved.</response>
  /// <returns>No content.</returns>
  [HttpPost("Announcement")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public IActionResult SetAnnouncement([FromBody] AnnouncementDto dto)
  {
    var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin is not initialized.");
    var level = dto?.Level?.ToLowerInvariant();
    plugin.Configuration.AnnouncementText = (dto?.Text ?? string.Empty).Trim();
    plugin.Configuration.AnnouncementLevel = level is "green" or "yellow" or "red" ? level : "green";

    // Replace the announcement's target groups. Empty means "global" (shown to everyone).
    plugin.Configuration.AnnouncementGroupIds.Clear();
    if (dto?.GroupIds is { } groupIds)
    {
      foreach (var groupId in groupIds)
      {
        if (groupId != Guid.Empty && !plugin.Configuration.AnnouncementGroupIds.Contains(groupId))
        {
          plugin.Configuration.AnnouncementGroupIds.Add(groupId);
        }
      }
    }

    plugin.SaveConfiguration();
    return NoContent();
  }
}
