using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Creates requests through the one path every request takes — access and scope rules, parental
/// restriction, duplicate and quota checks, approval, then notification and dispatch — whether a user
/// clicked "Request", an administrator requested on their behalf, or Jelly Crowd requested on its own.
/// </summary>
public interface IRequestCreationService
{
  /// <summary>
  /// Creates a request for a user, under every rule that applies to them: requests enabled, media type and
  /// granularity offered, parental restriction, duplicates, whole-quota size, request limit, approval.
  /// </summary>
  /// <param name="userId">The user the request is for.</param>
  /// <param name="dto">The request payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The outcome, and the request when one was recorded.</returns>
  Task<RequestCreationResult> CreateAsync(Guid userId, CreateRequestDto dto, CancellationToken cancellationToken);

  /// <summary>
  /// Creates a request a parent makes for one of their children: the request is the child's — on the
  /// child's quota, under the child's limits and approval rules — and only a title suited to the child's
  /// age goes through. A child account requests nothing itself; this is how titles reach them.
  /// </summary>
  /// <param name="parentId">The parent making the request.</param>
  /// <param name="childId">The child the request is for.</param>
  /// <param name="dto">The request payload.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The outcome, and the request when one was recorded.</returns>
  Task<RequestCreationResult> CreateForChildAsync(Guid parentId, Guid childId, CreateRequestDto dto, CancellationToken cancellationToken);

  /// <summary>
  /// Creates a request an administrator makes on a user's behalf: the instance scope, duplicates and
  /// whole-quota size still apply, but not the user's own limits, and the administrator picks the status.
  /// </summary>
  /// <param name="dto">The request payload, with its target user.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The outcome, and the request when one was recorded.</returns>
  Task<RequestCreationResult> CreateOnBehalfAsync(AdminCreateRequestDto dto, CancellationToken cancellationToken);
}
