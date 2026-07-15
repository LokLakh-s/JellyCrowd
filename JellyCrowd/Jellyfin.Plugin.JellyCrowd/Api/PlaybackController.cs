using System;
using System.Net.Mime;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Playback-time helpers for the injected web client. Small, read-only, per-item lookups.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyCrowd/Playback")]
[Produces(MediaTypeNames.Application.Json)]
public class PlaybackController : ControllerBase
{
  private readonly IOutroSegmentStore _outroSegmentStore;
  private readonly IOutroStore _outroStore;
  private readonly ILibraryManager _libraryManager;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="PlaybackController"/> class.
  /// </summary>
  /// <param name="outroSegmentStore">The fingerprinted end-credits cache.</param>
  /// <param name="outroStore">The brightness/silence outro cache.</param>
  /// <param name="libraryManager">The library manager (resolves the item's runtime).</param>
  /// <param name="config">The plugin configuration accessor.</param>
  public PlaybackController(
    IOutroSegmentStore outroSegmentStore,
    IOutroStore outroStore,
    ILibraryManager libraryManager,
    Func<PluginConfiguration> config)
  {
    _outroSegmentStore = outroSegmentStore;
    _outroStore = outroStore;
    _libraryManager = libraryManager;
    _config = config;
  }

  /// <summary>
  /// Gets the end-credits region of an item (and its runtime) so the client can offer a smarter Skip
  /// Outro — advancing to the next episode, or jumping to a post-credits bonus. Empty (204) when the
  /// feature is off, the item has no outro, or its runtime is unknown.
  /// </summary>
  /// <param name="itemId">The playing item's Jellyfin id.</param>
  /// <response code="200">The outro region and runtime.</response>
  /// <response code="204">No outro to offer for this item.</response>
  /// <returns>The outro region, or no content.</returns>
  [HttpGet("Outro/{itemId:guid}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public ActionResult<OutroSkipDto> Outro(Guid itemId)
  {
    if (!_config().SkipOutroEnabled)
    {
      return NoContent();
    }

    var region = OutroRegionResolver.Resolve(_outroSegmentStore.Get(itemId), _outroStore.Get(itemId));
    if (region is null)
    {
      return NoContent();
    }

    var runtime = _libraryManager.GetItemById(itemId)?.RunTimeTicks ?? 0;
    if (runtime <= 0)
    {
      return NoContent();
    }

    return Ok(new OutroSkipDto
    {
      OutroStartTicks = region.StartTicks,
      OutroEndTicks = region.EndTicks,
      RunTimeTicks = runtime
    });
  }
}
