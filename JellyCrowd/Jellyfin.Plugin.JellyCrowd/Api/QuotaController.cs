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
[ServiceFilter(typeof(PluginVisibilityFilter))]
[ServiceFilter(typeof(RateLimitFilter))]
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
  /// Lists every user's quota usage (admins only), for the per-user quota view in the config page.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The per-user usage snapshots.</response>
  /// <returns>Each requesting user's used/quota bytes.</returns>
  [HttpGet("All")]
  [Authorize(Policy = "RequiresElevation")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<UserQuotaInfoDto>>> All(CancellationToken cancellationToken)
  {
    var requests = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var userIds = requests.Select(r => r.UserId).Distinct().ToList();

    var result = new List<UserQuotaInfoDto>();
    foreach (var userId in userIds)
    {
      var info = await _quotaService.GetUsageAsync(userId, cancellationToken).ConfigureAwait(false);
      result.Add(new UserQuotaInfoDto
      {
        UserId = userId,
        UsedBytes = info.UsedBytes,
        QuotaBytes = info.QuotaBytes,
        Unlimited = info.Unlimited,
        Tier = info.Tier
      });
    }

    return Ok(result);
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
    var retentionHours = Plugin.Instance?.Configuration.DeletionRetentionHours ?? 0;
    var expiryDays = Plugin.Instance?.Configuration.MediaExpiryDays ?? 0;

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
        Episode = r.Episode,
        JellyfinItemId = r.JellyfinItemId,
        SizeBytes = _libraryMatcher.GetSizeBytes(r.MediaType, r.TmdbId),
        DeletionRequestedAt = r.DeletionRequestedAt,
        DeletionAt = r.DeletionRequestedAt?.AddHours(retentionHours),
        ExpiresAt = (expiryDays > 0 && r.AvailableAt is { } at) ? at.AddDays(expiryDays) : null
      })
      .ToList();

    return Ok(media);
  }
}
