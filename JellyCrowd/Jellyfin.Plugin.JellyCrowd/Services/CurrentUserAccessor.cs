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
}
