namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Deletes a library item (and its files) from Jellyfin. Abstracted so the destructive call is
/// isolated and the deletion task stays testable.
/// </summary>
public interface IMediaDeleter
{
  /// <summary>
  /// Deletes the library item with the given id, removing its files from disk.
  /// </summary>
  /// <param name="jellyfinItemId">The Jellyfin library item id (32-char hex).</param>
  /// <returns><c>true</c> if an item was found and deleted.</returns>
  bool Delete(string jellyfinItemId);
}
