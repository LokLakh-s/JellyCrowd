using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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

    return File(stream, ContentTypeFor(path));
  }

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

    return "application/octet-stream";
  }
}
