using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Locates the pre-roll folder(s) for Local Intros and enumerates their video files. The
/// admin creates a folder with a known name (e.g. <c>intros</c>) beside their media; this finds it from the
/// existing libraries' locations (a sibling of a library folder, or a subfolder of one), so no path has to
/// be configured by hand.
/// </summary>
public static class LocalIntrosDiscovery
{
  private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v", ".wmv", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg",
  };

  /// <summary>
  /// Finds existing folders named <paramref name="folderName"/> at or beside the server's library roots.
  /// </summary>
  /// <param name="libraryManager">The library manager (source of library locations).</param>
  /// <param name="folderName">The pre-roll folder name to look for.</param>
  /// <returns>The distinct matching folder paths that exist on disk.</returns>
  public static IReadOnlyList<string> FindFolders(ILibraryManager libraryManager, string folderName)
  {
    ArgumentNullException.ThrowIfNull(libraryManager);

    var found = new List<string>();
    if (string.IsNullOrWhiteSpace(folderName))
    {
      return found;
    }

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var virtualFolder in libraryManager.GetVirtualFolders())
    {
      foreach (var location in virtualFolder.Locations)
      {
        // A pre-roll folder can sit beside a library folder (its parent) or inside one (the location).
        foreach (var root in new[] { location, Path.GetDirectoryName(location) })
        {
          if (string.IsNullOrEmpty(root))
          {
            continue;
          }

          var candidate = Path.Combine(root, folderName);
          if (seen.Add(candidate) && Directory.Exists(candidate))
          {
            found.Add(candidate);
          }
        }
      }
    }

    return found;
  }

  /// <summary>
  /// Enumerates the pre-roll video files in the discovered folder(s), in a stable order. The caller registers
  /// them as playable items (see <see cref="IIntroFileRegistry"/>).
  /// </summary>
  /// <param name="libraryManager">The library manager (source of library locations).</param>
  /// <param name="folderName">The pre-roll folder name.</param>
  /// <returns>The pre-roll video file paths.</returns>
  public static IReadOnlyList<string> FindPrerollFiles(ILibraryManager libraryManager, string folderName)
  {
    ArgumentNullException.ThrowIfNull(libraryManager);

    var files = new List<string>();
    foreach (var folder in FindFolders(libraryManager, folderName))
    {
      files.AddRange(EnumerateVideos(folder));
    }

    return files;
  }

  private static IEnumerable<string> EnumerateVideos(string folder)
  {
    try
    {
      if (!Directory.Exists(folder))
      {
        return Enumerable.Empty<string>();
      }

      return Directory.EnumerateFiles(folder)
        .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToList();
    }
#pragma warning disable CA1031 // A bad folder just means no pre-roll, never a crash.
    catch (Exception)
#pragma warning restore CA1031
    {
      return Enumerable.Empty<string>();
    }
  }
}
