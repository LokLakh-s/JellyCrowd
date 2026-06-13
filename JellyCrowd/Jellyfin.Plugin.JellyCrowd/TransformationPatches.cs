using System;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd;

/// <summary>
/// File Transformation callbacks. Invoked by reflection by the File Transformation plugin, which
/// passes the current file contents and uses the returned string as the new contents.
/// </summary>
public static class TransformationPatches
{
  private const string HeaderScriptUrl = "/JellyCrowd/Web/header.js";
  private const string ScriptTag = "<script src=\"" + HeaderScriptUrl + "\" defer></script>";

  /// <summary>
  /// Injects the Jelly Crowd header script into the Jellyfin web client's index.html.
  /// </summary>
  /// <param name="request">The transformation request carrying the file contents.</param>
  /// <returns>The transformed contents.</returns>
  public static string InjectHeader(TransformationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var contents = request.Contents ?? string.Empty;
    if (contents.Contains(HeaderScriptUrl, StringComparison.Ordinal))
    {
      return contents;
    }

    var index = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
    return index < 0 ? contents : contents.Insert(index, ScriptTag);
  }
}
