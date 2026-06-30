using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Adds <see cref="WebInjectionMiddleware"/> to Jellyfin's request pipeline. Jellyfin runs every
/// <see cref="IStartupFilter"/> registered by a plugin, which is how a plugin contributes middleware
/// without the host exposing the pipeline directly (the same hook the File Transformation plugin uses).
/// </summary>
public sealed class WebInjectionStartupFilter : IStartupFilter
{
  /// <inheritdoc />
  public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
  {
    ArgumentNullException.ThrowIfNull(next);
    return app =>
    {
      app.UseMiddleware<WebInjectionMiddleware>();
      next(app);
    };
  }
}
