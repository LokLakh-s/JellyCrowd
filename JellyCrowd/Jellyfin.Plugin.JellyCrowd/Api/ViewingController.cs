using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// The current user's viewing preferences — preferred version (original, dubbed, original with subtitles)
/// and subtitle languages. Saving them also sets the matching Jellyfin playback settings.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/Viewing")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
public partial class ViewingController : ControllerBase
{
  // A user picks a handful of subtitle languages at most.
  private const int MaxSubtitleLanguages = 5;

  private readonly IUserPrefsStore _prefs;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly IUserManager _userManager;
  private readonly LanguageCodes _codes;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<ViewingController> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="ViewingController"/> class.
  /// </summary>
  /// <param name="prefs">The user preferences store.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="userManager">The user manager (playback settings).</param>
  /// <param name="codes">Language codes and the dub language.</param>
  /// <param name="config">The plugin configuration accessor.</param>
  /// <param name="logger">The logger.</param>
  public ViewingController(
    IUserPrefsStore prefs,
    ICurrentUserAccessor userAccessor,
    IUserManager userManager,
    LanguageCodes codes,
    Func<PluginConfiguration> config,
    ILogger<ViewingController> logger)
  {
    _prefs = prefs;
    _userAccessor = userAccessor;
    _userManager = userManager;
    _codes = codes;
    _config = config;
    _logger = logger;
  }

  /// <summary>
  /// Gets the caller's viewing preferences and what is offered.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The preferences.</response>
  /// <returns>The caller's preferences and what is offered.</returns>
  [HttpGet("Mine")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<ViewingPrefsDto>> Mine(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var prefs = await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false);
    var config = _config();
    return Ok(new ViewingPrefsDto
    {
      LanguagePreferencesAvailable = config.LanguagePreferencesEnabled,
      SubtitleDownloadsAvailable = config.SubtitleDownloadsEnabled,
      DubLanguage = _codes.DubLanguage(config),
      LanguagePreference = LanguagePolicy.Normalize(prefs.LanguagePreference),
      SubtitleLanguages = new Collection<string>(prefs.SubtitleLanguages.ToList())
    });
  }

  /// <summary>
  /// Saves the caller's viewing preferences, and sets the Jellyfin playback settings they stand for.
  /// Parts the administrator does not offer are left as they are.
  /// </summary>
  /// <param name="dto">The preferences.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The saved preferences.</response>
  /// <response code="400">An unknown version or language.</response>
  /// <returns>The preferences as saved.</returns>
  [HttpPost("Mine")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<ViewingPrefsDto>> SetMine([FromBody] ViewingPrefsDto dto, CancellationToken cancellationToken)
  {
    if (dto is null)
    {
      return BadRequest("Preferences are required.");
    }

    var preference = LanguagePolicy.Normalize(dto.LanguagePreference);
    if (preference.Length == 0 && !string.IsNullOrWhiteSpace(dto.LanguagePreference))
    {
      return BadRequest("The version must be 'original', 'dubbed', 'subtitled' or empty.");
    }

    var languages = (dto.SubtitleLanguages ?? new Collection<string>())
      .Select(l => (l ?? string.Empty).Trim().ToLowerInvariant())
      .Distinct(StringComparer.Ordinal)
      .ToList();
    if (languages.Count > MaxSubtitleLanguages || languages.Any(l => !TwoLetterCode().IsMatch(l)))
    {
      return BadRequest("Subtitle languages must be up to five two-letter codes.");
    }

    var config = _config();
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);

    // Read-modify-write: the notification preferences share this record.
    var prefs = await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false);
    prefs.UserId = userId;
    if (config.LanguagePreferencesEnabled)
    {
      prefs.LanguagePreference = preference;
    }

    if (config.SubtitleDownloadsEnabled)
    {
      prefs.SubtitleLanguages = new Collection<string>(languages);
    }

    await _prefs.SetAsync(prefs, cancellationToken).ConfigureAwait(false);
    await ApplyPlaybackAsync(userId, config, prefs).ConfigureAwait(false);
    return await Mine(cancellationToken).ConfigureAwait(false);
  }

  [GeneratedRegex("^[a-z]{2}$")]
  private static partial Regex TwoLetterCode();

  // Writes the playback settings the preferences stand for into the user's Jellyfin configuration. Best
  // effort: a failure here leaves the saved preferences in place (they still drive the requests).
  private async Task ApplyPlaybackAsync(Guid userId, PluginConfiguration config, UserNotificationPrefs prefs)
  {
    try
    {
      var user = _userManager.GetUserById(userId);
      var configuration = user is null ? null : _userManager.GetUserDto(user, string.Empty)?.Configuration;
      if (configuration is null)
      {
        return;
      }

      var dub3 = _codes.ThreeLetter(_codes.DubLanguage(config));
      var firstSubtitle = config.SubtitleDownloadsEnabled ? prefs.SubtitleLanguages.FirstOrDefault() : null;
      var settings = LanguagePolicy.PlaybackFor(
        config.LanguagePreferencesEnabled ? prefs.LanguagePreference : string.Empty,
        dub3 ?? string.Empty,
        firstSubtitle is null ? null : _codes.ThreeLetter(firstSubtitle));
      if (settings.SetAudio)
      {
        configuration.AudioLanguagePreference = settings.AudioLanguage;
        configuration.PlayDefaultAudioTrack = settings.PlayDefaultAudioTrack;
      }

      if (!string.IsNullOrEmpty(settings.SubtitleLanguage))
      {
        configuration.SubtitleLanguagePreference = settings.SubtitleLanguage;
      }

      if (settings.AlwaysSubtitles)
      {
        configuration.SubtitleMode = SubtitlePlaybackMode.Always;
      }

      await _userManager.UpdateConfigurationAsync(userId, configuration).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // The preferences are saved; the playback settings are a convenience.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd: could not set the playback settings of user {UserId}.", userId);
    }
  }
}
