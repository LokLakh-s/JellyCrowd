using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Who owns which media, and the admin's changes to it. Owning a media is holding an available request for
/// it: it counts in the member's quota and expires with it; it never decides whether the file exists.
/// </summary>
public interface IOwnershipService
{
  /// <summary>
  /// Lists the library's media (movies, and shows season by season) with their owners.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The media, by title.</returns>
  Task<IReadOnlyList<OwnedMediaDto>> ListAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Gives a media already in the library to a member, telling them; one they already own is renewed.
  /// </summary>
  /// <param name="userId">The member.</param>
  /// <param name="media">The media.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>Their ownership, or <c>null</c> when the media is not in the library.</returns>
  Task<OwnershipGrant?> GiveOneAsync(Guid userId, OwnershipMediaRef media, CancellationToken cancellationToken);

  /// <summary>
  /// Gives every media to every member.
  /// </summary>
  /// <param name="userIds">The members.</param>
  /// <param name="media">The media.</param>
  /// <param name="adminName">Who made the change, for the activity log.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>What changed.</returns>
  Task<OwnershipChangeResult> GiveAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<OwnershipMediaRef> media, string adminName, CancellationToken cancellationToken);

  /// <summary>
  /// Takes every media from every member, silently: their quota is freed, the files stay. A season taken
  /// from a member who owns the whole show leaves them its other seasons.
  /// </summary>
  /// <param name="userIds">The members.</param>
  /// <param name="media">The media.</param>
  /// <param name="adminName">Who made the change, for the activity log.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>What changed.</returns>
  Task<OwnershipChangeResult> RemoveAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<OwnershipMediaRef> media, string adminName, CancellationToken cancellationToken);
}
