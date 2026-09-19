using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Where the user guide's content and screenshots come from. The plugin is distributed to everyone, so
/// what it ships describes Jelly Crowd without naming a server or showing anyone's library; an instance
/// that wants its own guide puts the files in its data folder and they win, file by file.
/// <para>
/// The lookup and the name validation live here rather than in the controller so they can be tested
/// against a real folder without a running plugin.
/// </para>
/// </summary>
public static class GuideAssets
{
  /// <summary>The folder, inside the plugin's data folder, an instance puts its own guide in.</summary>
  public const string OverrideFolder = "guide";

  /// <summary>The content document's file name, in the override folder and among the embedded assets.</summary>
  public const string ContentFileName = "guide-content.json";

  // A plain file name with a known image extension: no directory separator, no "..", nothing that could
  // walk out of the guide folder. Anything else is refused before a path is ever built from it.
  private static readonly Regex SafeImageName = new(
    "^[a-z0-9][a-z0-9._-]{0,63}\\.(png|jpg|jpeg|webp|gif|svg)$",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

  private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
  {
    [".png"] = "image/png",
    [".jpg"] = "image/jpeg",
    [".jpeg"] = "image/jpeg",
    [".webp"] = "image/webp",
    [".gif"] = "image/gif",
    [".svg"] = "image/svg+xml"
  };

  /// <summary>
  /// Whether a requested image name is one we are willing to look for on disk.
  /// </summary>
  /// <param name="name">The name from the request.</param>
  /// <returns><c>true</c> when the name is a plain image file name.</returns>
  public static bool IsSafeImageName(string? name)
    => !string.IsNullOrEmpty(name) && !name.Contains("..", StringComparison.Ordinal) && SafeImageName.IsMatch(name);

  /// <summary>
  /// The media type to serve an image name as.
  /// </summary>
  /// <param name="name">The image file name.</param>
  /// <returns>The media type, or the generic binary type for anything unknown.</returns>
  public static string ImageContentType(string name)
    => ImageTypes.TryGetValue(Path.GetExtension(name ?? string.Empty), out var known) ? known : "application/octet-stream";

  /// <summary>
  /// The instance's own content document, or <c>null</c> when it has none (so the plugin's is served).
  /// </summary>
  /// <param name="dataFolderPath">The plugin's data folder, or <c>null</c> when there is none yet.</param>
  /// <returns>An existing file path, or <c>null</c>.</returns>
  public static string? CustomContentPath(string? dataFolderPath)
    => ExistingFile(dataFolderPath, ContentFileName);

  /// <summary>
  /// The instance's own copy of an image, or <c>null</c> when it has none.
  /// </summary>
  /// <param name="dataFolderPath">The plugin's data folder, or <c>null</c> when there is none yet.</param>
  /// <param name="name">The image file name; refused unless <see cref="IsSafeImageName"/> accepts it.</param>
  /// <returns>An existing file path, or <c>null</c>.</returns>
  public static string? CustomImagePath(string? dataFolderPath, string? name)
    => IsSafeImageName(name) ? ExistingFile(dataFolderPath, Path.Combine("img", name!)) : null;

  [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "The only caller-supplied fragment is an image name already whitelist-validated by IsSafeImageName (a plain file name with a known image extension, no separators or '..'), so the path cannot leave the guide folder.")]
  private static string? ExistingFile(string? dataFolderPath, string relative)
  {
    if (string.IsNullOrEmpty(dataFolderPath))
    {
      return null;
    }

    var path = Path.Combine(dataFolderPath, OverrideFolder, relative);
    return File.Exists(path) ? path : null;
  }
}
