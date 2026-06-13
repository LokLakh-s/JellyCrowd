using System;
using System.Net.Mime;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
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

  /// <summary>
  /// Initializes a new instance of the <see cref="SettingsController"/> class.
  /// </summary>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  public SettingsController(Func<PluginConfiguration> config)
  {
    _config = config;
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
    var language = _config().Language;
    return Ok(new LanguageSettingDto { Language = string.IsNullOrWhiteSpace(language) ? "auto" : language });
  }
}
