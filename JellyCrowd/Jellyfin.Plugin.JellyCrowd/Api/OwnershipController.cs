using System;
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
/// Admin view of who owns which media in the library, and bulk changes to it.
/// </summary>
[ApiController]
[Route("JellyCrowd/Ownership")]
[Authorize(Policy = "RequiresElevation")]
[Produces(MediaTypeNames.Application.Json)]
[ServiceFilter(typeof(PluginVisibilityFilter))]
public class OwnershipController : ControllerBase
{
  /// <summary>
  /// The most members × media one change may touch, so a stray request cannot rewrite the whole store at once.
  /// </summary>
  internal const int MaxPairs = 5000;

  private readonly IOwnershipService _ownership;
  private readonly ICurrentUserAccessor _userAccessor;
  private readonly Func<Guid, string> _resolveUserName;

  /// <summary>
  /// Initializes a new instance of the <see cref="OwnershipController"/> class.
  /// </summary>
  /// <param name="ownership">The ownership service.</param>
  /// <param name="userAccessor">The current-user accessor (who makes the change).</param>
  /// <param name="resolveUserName">Resolves a user id to a display name for the activity log.</param>
  public OwnershipController(IOwnershipService ownership, ICurrentUserAccessor userAccessor, Func<Guid, string> resolveUserName)
  {
    _ownership = ownership;
    _userAccessor = userAccessor;
    _resolveUserName = resolveUserName;
  }

  /// <summary>
  /// Lists the library's media (movies, and shows season by season) with their owners.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The media, by title.</response>
  /// <returns>The media and their owners.</returns>
  [HttpGet]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<OwnedMediaDto>>> List(CancellationToken cancellationToken)
    => Ok(await _ownership.ListAsync(cancellationToken).ConfigureAwait(false));

  /// <summary>
  /// Gives media of the library to members. Each member is told what was added to their library; a media
  /// they already own is renewed.
  /// </summary>
  /// <param name="dto">The members and the media.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">What changed.</response>
  /// <response code="400">No member or no media, or too many at once.</response>
  /// <returns>The counts of the change.</returns>
  [HttpPost("Give")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<OwnershipChangeResult>> Give([FromBody] OwnershipChangeDto dto, CancellationToken cancellationToken)
  {
    var error = Validate(dto);
    if (error is not null)
    {
      return BadRequest(error);
    }

    var admin = await AdminNameAsync().ConfigureAwait(false);
    return Ok(await _ownership.GiveAsync(dto.UserIds, dto.Media, admin, cancellationToken).ConfigureAwait(false));
  }

  /// <summary>
  /// Takes media from members, silently: their quota is freed and the files stay in the library.
  /// </summary>
  /// <param name="dto">The members and the media.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">What changed.</response>
  /// <response code="400">No member or no media, or too many at once.</response>
  /// <returns>The counts of the change.</returns>
  [HttpPost("Remove")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<OwnershipChangeResult>> Remove([FromBody] OwnershipChangeDto dto, CancellationToken cancellationToken)
  {
    var error = Validate(dto);
    if (error is not null)
    {
      return BadRequest(error);
    }

    var admin = await AdminNameAsync().ConfigureAwait(false);
    return Ok(await _ownership.RemoveAsync(dto.UserIds, dto.Media, admin, cancellationToken).ConfigureAwait(false));
  }

  private static string? Validate(OwnershipChangeDto? dto)
  {
    if (dto?.UserIds is null || !dto.UserIds.Any(u => u != Guid.Empty))
    {
      return "At least one member is required.";
    }

    if (dto.Media is null || dto.Media.Count == 0)
    {
      return "At least one media is required.";
    }

    return (long)dto.UserIds.Count * dto.Media.Count > MaxPairs
      ? "Too many members × media at once (" + MaxPairs + " at most)."
      : null;
  }

  private async Task<string> AdminNameAsync()
    => _resolveUserName(await _userAccessor.GetUserIdAsync(Request).ConfigureAwait(false));
}
