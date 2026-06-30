using System;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Injects the Jelly Crowd shell script into the Jellyfin web client's <c>index.html</c> at request time,
/// so the plugin no longer depends on the external File Transformation plugin. It serves the (static)
/// index.html itself with the script added — read-only (no on-disk patching), so it survives web-client
/// updates and works on read-only deployments. Every other request passes straight through untouched.
/// </summary>
public sealed class WebInjectionMiddleware
{
  private readonly RequestDelegate _next;
  private readonly IServerApplicationPaths _paths;
  private readonly ILogger<WebInjectionMiddleware> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="WebInjectionMiddleware"/> class.
  /// </summary>
  /// <param name="next">The next middleware.</param>
  /// <param name="paths">The server paths (to locate the web client's index.html).</param>
  /// <param name="logger">The logger.</param>
  public WebInjectionMiddleware(RequestDelegate next, IServerApplicationPaths paths, ILogger<WebInjectionMiddleware> logger)
  {
    _next = next;
    _paths = paths;
    _logger = logger;
  }

  /// <summary>
  /// Serves the injected index.html for the web-client entry requests; passes everything else through.
  /// </summary>
  /// <param name="context">The HTTP context.</param>
  /// <returns>A task that completes when the request has been handled.</returns>
  public async Task InvokeAsync(HttpContext context)
  {
    if (HttpMethods.IsGet(context.Request.Method) && IsIndexRequest(context.Request.Path))
    {
      var html = await TryBuildInjectedIndexAsync(context).ConfigureAwait(false);
      if (html is not null)
      {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, context.RequestAborted).ConfigureAwait(false);
        return;
      }
    }

    await _next(context).ConfigureAwait(false);
  }

  // The web client's entry document: "…/web/" or "…/web/index.html" (avoid "/web" without a slash, which
  // Jellyfin redirects). Client-side routing uses the hash, so the server path is always one of these.
  private static bool IsIndexRequest(PathString path)
  {
    var value = path.Value ?? string.Empty;
    return value.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
      || value.EndsWith("/web/", StringComparison.OrdinalIgnoreCase);
  }

  private async Task<string?> TryBuildInjectedIndexAsync(HttpContext context)
  {
    try
    {
      var file = Path.Combine(_paths.WebPath, "index.html");
      if (!File.Exists(file))
      {
        return null;
      }

      var html = await File.ReadAllTextAsync(file, context.RequestAborted).ConfigureAwait(false);
      return WebInjection.InjectScript(html);
    }
#pragma warning disable CA1031 // On any failure, fall through so Jellyfin serves index.html normally (overlay absent, nothing broken).
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd: could not serve the injected index.html; falling back to the default.");
      return null;
    }
  }
}
