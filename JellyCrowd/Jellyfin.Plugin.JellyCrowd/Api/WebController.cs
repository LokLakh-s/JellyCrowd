using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Serves the plugin's embedded user-facing web assets (HTML, JS, CSS, i18n catalogs)
/// under <c>/JellyCrowd/Web/...</c>. Assets are public UI (no secrets), so anonymous access is allowed.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("JellyCrowd/Web")]
public class WebController : ControllerBase
{
  private const string ResourcePrefix = "Jellyfin.Plugin.JellyCrowd.Web.";

  // The plugin build these assets were compiled into. Embedded resources cannot change without it, so it
  // is exactly the right cache validator.
  private static readonly string PluginVersion =
    typeof(WebController).Assembly.GetName().Version?.ToString() ?? "0";

  /// <summary>
  /// Returns an embedded web asset by relative path (e.g. <c>catalog.html</c>, <c>strings/en.json</c>).
  /// </summary>
  /// <param name="path">The asset path relative to the <c>Web</c> folder.</param>
  /// <response code="200">The asset content.</response>
  /// <response code="404">No such asset.</response>
  /// <returns>The asset stream, or 404.</returns>
  [HttpGet("{*path}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public IActionResult GetAsset(string? path)
  {
    if (string.IsNullOrEmpty(path) || !IsSafe(path))
    {
      return NotFound();
    }

    var resourceName = ResourcePrefix + path.Replace('/', '.');
    var assembly = typeof(WebController).Assembly;
    var stream = assembly.GetManifestResourceStream(resourceName);
    if (stream is null)
    {
      return NotFound();
    }

    // Without a validator the browser was free to keep an asset indefinitely and had no way to learn the
    // plugin had moved on: a page could run this version's scripts against the previous version's
    // translation catalog, rendering the keys it did not know yet ("request_button") until the user hit
    // Ctrl+F5. Revalidating on every request costs one 304 and cannot go stale.
    Response.Headers.CacheControl = "no-cache";
    return File(stream, ContentTypeFor(path), lastModified: null, entityTag: new EntityTagHeaderValue(AssetETag(PluginVersion, path)));
  }

  /// <summary>
  /// Builds the cache validator for an embedded asset. It changes when the plugin version changes, so
  /// updating the plugin invalidates every cached script, fragment and translation catalog at once.
  /// </summary>
  /// <param name="version">The plugin assembly version.</param>
  /// <param name="path">The asset path, already validated by <see cref="IsSafe"/>.</param>
  /// <returns>A quoted entity tag.</returns>
  internal static string AssetETag(string version, string path)
    => "\"" + version + "/" + path + "\"";

  private static bool IsSafe(string path)
  {
    if (path.Contains("..", StringComparison.Ordinal))
    {
      return false;
    }

    foreach (var c in path)
    {
      var ok = char.IsLetterOrDigit(c) || c == '.' || c == '/' || c == '_' || c == '-';
      if (!ok)
      {
        return false;
      }
    }

    return true;
  }

  private static string ContentTypeFor(string path)
  {
    if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
    {
      return "text/html; charset=utf-8";
    }

    if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
    {
      return "text/javascript; charset=utf-8";
    }

    if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
    {
      return "text/css; charset=utf-8";
    }

    if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
      return "application/json; charset=utf-8";
    }

    if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
    {
      return "image/png";
    }

    // The guide's screenshots. They must carry a real image type: the responses go out with
    // X-Content-Type-Options: nosniff on most setups, so an octet-stream would simply not render.
    if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
    {
      return "image/jpeg";
    }

    if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
    {
      return "image/webp";
    }

    return "application/octet-stream";
  }
}
