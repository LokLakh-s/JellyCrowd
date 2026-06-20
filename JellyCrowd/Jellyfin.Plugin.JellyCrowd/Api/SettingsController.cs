using System;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
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

  /// <summary>
  /// Initializes a new instance of the <see cref="SettingsController"/> class.
  /// </summary>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="userAccessor">The current-user accessor (to resolve administrator status).</param>
  public SettingsController(Func<PluginConfiguration> config, ICurrentUserAccessor userAccessor)
  {
    _config = config;
    _userAccessor = userAccessor;
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
      Hidden = config.HiddenFromUsers
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
    var visible = !_config().HiddenFromUsers || isAdmin;
    return Ok(new VisibilitySettingDto { Visible = visible });
  }
}
