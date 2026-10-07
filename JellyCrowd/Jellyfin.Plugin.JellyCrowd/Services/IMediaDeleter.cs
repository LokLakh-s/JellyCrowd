using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Deletes a library item (and its files) from Jellyfin. Abstracted so the destructive call is
/// isolated and the deletion task stays testable.
/// </summary>
public interface IMediaDeleter
{
  /// <summary>
  /// Gets a value indicating whether a library scan is running: one that listed a folder before it was deleted
  /// puts the item back (an empty shell), so deleting waits for it to end.
  /// </summary>
  bool ScanRunning { get; }

  /// <summary>
  /// Deletes the library item with the given id, removing its files from disk.
  /// </summary>
  /// <param name="jellyfinItemId">The Jellyfin library item id (32-char hex).</param>
  /// <returns><c>true</c> if an item was found and deleted.</returns>
  bool Delete(string jellyfinItemId);

  /// <summary>
  /// Determines whether a library item with the given id exists, so its deletion can be counted on.
  /// </summary>
  /// <param name="jellyfinItemId">The Jellyfin library item id.</param>
  /// <returns><c>true</c> if the item is in the library.</returns>
  bool Exists(string jellyfinItemId);

  /// <summary>
  /// Lists the movies, series and seasons of the given libraries: what a cleanup limited to them may delete.
  /// </summary>
  /// <param name="libraryIds">The Jellyfin library ids.</param>
  /// <returns>Their item ids.</returns>
  IReadOnlySet<Guid> ItemsIn(IReadOnlyCollection<string> libraryIds);
}
