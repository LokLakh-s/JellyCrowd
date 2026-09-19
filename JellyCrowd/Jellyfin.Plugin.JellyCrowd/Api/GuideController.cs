using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Serves the user guide's content and screenshots, letting an instance replace them with its own.
/// <para>
/// The plugin ships a guide that describes Jelly Crowd itself and names nobody: it is distributed to
/// everyone, so it must not carry one server's identity or screenshots of one server's library. An
/// administrator who wants a guide tailored to their instance (their name, their captures) drops
/// <c>guide-content.json</c> and an <c>img</c> folder into <c>&lt;plugin data folder&gt;/guide/</c>;
/// what is there wins, file by file, and never leaves that server.
/// </para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("JellyCrowd/Guide")]
public class GuideController : ControllerBase
{
  private const string EmbeddedContent = "Jellyfin.Plugin.JellyCrowd.Web.guide-content.json";
  private const string EmbeddedImagePrefix = "Jellyfin.Plugin.JellyCrowd.Web.img.";

  /// <summary>
  /// Returns the guide's content: this instance's own if it has one, the plugin's otherwise.
  /// </summary>
  /// <response code="200">The guide content document.</response>
  /// <response code="404">The plugin ships no guide content (should not happen).</response>
  /// <returns>The JSON content document.</returns>
  [HttpGet("Content")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "The path is a constant file name inside the plugin's own data folder; nothing from the request takes part in it.")]
  public IActionResult GetContent()
  {
    var custom = GuideAssets.CustomContentPath(Plugin.Instance?.DataFolderPath);
    if (custom is not null)
    {
      Response.Headers.CacheControl = "no-cache";
      return PhysicalFile(custom, "application/json; charset=utf-8");
    }

    var stream = typeof(GuideController).Assembly.GetManifestResourceStream(EmbeddedContent);
    if (stream is null)
    {
      return NotFound();
    }

    Response.Headers.CacheControl = "no-cache";
    return File(stream, "application/json; charset=utf-8");
  }

  /// <summary>
  /// Returns one of the guide's screenshots, from this instance's own guide folder when it has one.
  /// </summary>
  /// <param name="name">The image file name, as the content document spells it.</param>
  /// <response code="200">The image.</response>
  /// <response code="404">No such image, here or in the plugin.</response>
  /// <returns>The image content.</returns>
  [HttpGet("Image/{name}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "The name is whitelist-validated by SafeImageName (a plain file name with a known image extension, no separators or '..') before any file access, so it cannot escape the guide folder.")]
  public IActionResult GetImage(string? name)
  {
    if (!GuideAssets.IsSafeImageName(name))
    {
      return NotFound();
    }

    var contentType = GuideAssets.ImageContentType(name!);

    var custom = GuideAssets.CustomImagePath(Plugin.Instance?.DataFolderPath, name);
    if (custom is not null)
    {
      Response.Headers.CacheControl = "no-cache";
      return PhysicalFile(custom, contentType);
    }

    // Embedded resource names flatten the folder, so Web/img/x.jpg is ...Web.img.x.jpg.
    var stream = typeof(GuideController).Assembly.GetManifestResourceStream(EmbeddedImagePrefix + name);
    if (stream is null)
    {
      return NotFound();
    }

    Response.Headers.CacheControl = "no-cache";
    return File(stream, contentType);
  }
}
