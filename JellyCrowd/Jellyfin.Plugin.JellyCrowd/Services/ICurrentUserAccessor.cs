using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Resolves the Jellyfin user behind the current request. Abstracted so controllers stay testable
/// without constructing Jellyfin authorization internals.
/// </summary>
public interface ICurrentUserAccessor
{
  /// <summary>
  /// Gets the authenticated user's identifier for the given request.
  /// </summary>
  /// <param name="request">The incoming HTTP request.</param>
  /// <returns>The user identifier, or <see cref="Guid.Empty"/> when unauthenticated.</returns>
  Task<Guid> GetUserIdAsync(HttpRequest request);
}
