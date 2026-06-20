using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="ICurrentUserAccessor"/> backed by Jellyfin's <see cref="IAuthorizationContext"/>.
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
  private readonly IAuthorizationContext _authorizationContext;

  /// <summary>
  /// Initializes a new instance of the <see cref="CurrentUserAccessor"/> class.
  /// </summary>
  /// <param name="authorizationContext">The Jellyfin authorization context.</param>
  public CurrentUserAccessor(IAuthorizationContext authorizationContext)
  {
    _authorizationContext = authorizationContext;
  }

  /// <inheritdoc />
  public async Task<Guid> GetUserIdAsync(HttpRequest request)
  {
    var info = await _authorizationContext.GetAuthorizationInfo(request).ConfigureAwait(false);
    return info.UserId;
  }

  /// <inheritdoc />
  public Task<bool> IsAdministratorAsync(HttpRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    // Jellyfin populates the "Administrator" role on the principal for admins (and API keys); this is
    // what the "RequiresElevation" policy checks under the hood.
    var isAdmin = request.HttpContext?.User?.IsInRole("Administrator") ?? false;
    return Task.FromResult(isAdmin);
  }
}
