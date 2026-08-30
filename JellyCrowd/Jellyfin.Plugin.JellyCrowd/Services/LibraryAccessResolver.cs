using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure resolution of the Jellyfin library ids to enable on a user's policy from an admin-chosen set,
/// keeping only ids that still correspond to an existing library. Jellyfin 10.11 stores enabled folders
/// as <see cref="Guid"/> values, while virtual folders expose their id as a string, so this also parses
/// and de-duplicates.
/// </summary>
public static class LibraryAccessResolver
{
  /// <summary>
  /// Resolves the requested library ids to the set of enabled-folder <see cref="Guid"/>s to write onto a
  /// user policy: only ids that parse as GUIDs and match an existing library are kept, de-duplicated and
  /// in first-seen order.
  /// </summary>
  /// <param name="requestedIds">The admin-chosen library ids (as strings).</param>
  /// <param name="existingItemIds">The ids of the libraries that currently exist (as strings).</param>
  /// <returns>The enabled-folder ids to set on the policy.</returns>
  public static Guid[] ResolveEnabledFolders(IEnumerable<string>? requestedIds, IEnumerable<string>? existingItemIds)
  {
    var existing = new HashSet<Guid>();
    if (existingItemIds is not null)
    {
      foreach (var id in existingItemIds)
      {
        if (Guid.TryParse(id, out var g))
        {
          existing.Add(g);
        }
      }
    }

    var result = new List<Guid>();
    var seen = new HashSet<Guid>();
    if (requestedIds is not null)
    {
      foreach (var id in requestedIds)
      {
        if (Guid.TryParse(id, out var g) && existing.Contains(g) && seen.Add(g))
        {
          result.Add(g);
        }
      }
    }

    return result.ToArray();
  }
}
