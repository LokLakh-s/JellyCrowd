using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Exposes the current user's disk-quota usage. Per-user quota overrides are managed by admins
/// through the plugin configuration page.
/// </summary>
[ApiController]
[Route("JellyCrowd/Quota")]
[Produces(MediaTypeNames.Application.Json)]
public class QuotaController : ControllerBase
{
  private readonly IQuotaService _quotaService;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly IRequestStore _store;
  private readonly ILibraryMatcher _libraryMatcher;

  /// <summary>
  /// Initializes a new instance of the <see cref="QuotaController"/> class.
  /// </summary>
  /// <param name="quotaService">The quota service.</param>
  /// <param name="userAccessor">The current-user accessor.</param>
  /// <param name="store">The request store.</param>
  /// <param name="libraryMatcher">The library matcher (resolves on-disk sizes).</param>
  public QuotaController(IQuotaService quotaService, ICurrentUserAccessor userAccessor, IRequestStore store, ILibraryMatcher libraryMatcher)
  {
    _quotaService = quotaService;
    _userAccessor = userAccessor;
    _store = store;
    _libraryMatcher = libraryMatcher;
  }

  /// <summary>
  /// Gets the current user's quota usage.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The usage snapshot.</response>
  /// <returns>The caller's used/quota bytes.</returns>
  [HttpGet("Me")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<QuotaInfo>> Me(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var info = await _quotaService.GetUsageAsync(userId, cancellationToken).ConfigureAwait(false);
    return Ok(info);
  }

  /// <summary>
  /// Lists the current user's available titles enriched with their on-disk size, for the "My media" view.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The caller's available media with sizes.</response>
  /// <returns>The available media items.</returns>
  [HttpGet("MyMedia")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<MediaUsageDto>>> MyMedia(CancellationToken cancellationToken)
  {
    var userId = await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false);
    var requests = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);

    var media = requests
      .Where(r => r.Status == RequestStatus.Available)
      .Select(r => new MediaUsageDto
      {
        RequestId = r.Id,
        MediaType = r.MediaType,
        TmdbId = r.TmdbId,
        Title = r.Title,
        PosterPath = r.PosterPath,
        Season = r.Season,
        JellyfinItemId = r.JellyfinItemId,
        SizeBytes = _libraryMatcher.GetSizeBytes(r.MediaType, r.TmdbId),
        DeletionRequestedAt = r.DeletionRequestedAt
      })
      .ToList();

    return Ok(media);
  }
}
