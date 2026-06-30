using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Stores and serves branding images uploaded from the admin's computer, so the admin can either point a
/// branding field at an external URL or upload a file. Images live in the plugin's data folder (server-side,
/// no browser cache) and are served back by the plugin itself.
/// </summary>
[ApiController]
[Route("JellyCrowd/Branding")]
public class BrandingController : ControllerBase
{
  private const long MaxUploadBytes = 8L * 1024 * 1024; // 8 MiB
  private const string SubFolder = "branding";

  // The exact shape Upload generates: a 32-char hex name + a known image extension. Anything else is rejected.
  private static readonly Regex SafeName = new("^[a-f0-9]{32}\\.(png|jpg|jpeg|gif|webp|svg|ico)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

  // Allowed image extensions -> content type. Anything else is rejected on upload and on serve.
  private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
  {
    [".png"] = "image/png",
    [".jpg"] = "image/jpeg",
    [".jpeg"] = "image/jpeg",
    [".gif"] = "image/gif",
    [".webp"] = "image/webp",
    [".svg"] = "image/svg+xml",
    [".ico"] = "image/x-icon"
  };

  /// <summary>
  /// Uploads a branding image. Administrators only.
  /// </summary>
  /// <param name="file">The image file (multipart/form-data).</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <response code="200">The stored image's relative URL (<c>{ "url": "JellyCrowd/Branding/Image/&lt;name&gt;" }</c>).</response>
  /// <response code="400">No file, too large, or an unsupported image type.</response>
  /// <returns>The relative URL the branding field should store.</returns>
  [HttpPost("Upload")]
  [Authorize(Policy = "RequiresElevation")]
  [RequestSizeLimit(MaxUploadBytes)]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
  {
    if (file is null || file.Length == 0)
    {
      return BadRequest("No file was uploaded.");
    }

    if (file.Length > MaxUploadBytes)
    {
      return BadRequest("The image is too large (max 8 MiB).");
    }

    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrEmpty(ext) || !AllowedTypes.ContainsKey(ext))
    {
      return BadRequest("Unsupported image type.");
    }

    var folder = GetFolder();
    Directory.CreateDirectory(folder);

    // Generated name (no caller-controlled path) keeps serving safe and avoids collisions.
    var name = Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
    var path = Path.Combine(folder, name);
    var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    await using (stream.ConfigureAwait(false))
    {
      await file.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    return Ok(new BrandingUploadResult { Url = "JellyCrowd/Branding/Image/" + name });
  }

  /// <summary>
  /// Serves a previously uploaded branding image. Anonymous (branding is shown to every visitor).
  /// </summary>
  /// <param name="name">The stored image file name.</param>
  /// <response code="200">The image.</response>
  /// <response code="404">No such image.</response>
  /// <returns>The image file.</returns>
  [HttpGet("Image/{name}")]
  [AllowAnonymous]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "The name is whitelist-validated by SafeName (32 hex chars + a known image extension, no separators or '..') before any file access, so it cannot escape the branding folder.")]
  public IActionResult Image(string name)
  {
    // Only accept the exact shape Upload generates; this blocks any path traversal.
    if (string.IsNullOrEmpty(name) || !SafeName.IsMatch(name))
    {
      return NotFound();
    }

    var ext = Path.GetExtension(name);
    if (string.IsNullOrEmpty(ext) || !AllowedTypes.TryGetValue(ext, out var contentType))
    {
      return NotFound();
    }

    var path = Path.Combine(GetFolder(), name);
    if (!System.IO.File.Exists(path))
    {
      return NotFound();
    }

    return PhysicalFile(path, contentType);
  }

  private static string GetFolder()
  {
    var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin is not initialized.");
    return Path.Combine(plugin.DataFolderPath, SubFolder);
  }
}
