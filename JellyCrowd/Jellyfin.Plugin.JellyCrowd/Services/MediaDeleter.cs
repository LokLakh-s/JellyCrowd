using System;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IMediaDeleter"/> backed by <see cref="ILibraryManager"/>.
/// </summary>
public sealed class MediaDeleter : IMediaDeleter
{
  private readonly ILibraryManager _libraryManager;
  private readonly ILogger<MediaDeleter> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="MediaDeleter"/> class.
  /// </summary>
  /// <param name="libraryManager">The Jellyfin library manager.</param>
  /// <param name="logger">The logger.</param>
  public MediaDeleter(ILibraryManager libraryManager, ILogger<MediaDeleter> logger)
  {
    _libraryManager = libraryManager;
    _logger = logger;
  }

  /// <inheritdoc />
  public bool Delete(string jellyfinItemId)
  {
    if (!Guid.TryParseExact(jellyfinItemId, "N", out var id) && !Guid.TryParse(jellyfinItemId, out id))
    {
      return false;
    }

    var item = _libraryManager.GetItemById(id);
    if (item is null)
    {
      _logger.LogWarning("Jelly Crowd deletion: library item {Id} not found.", id);
      return false;
    }

    _libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true, DeleteFromExternalProvider = false });
    _logger.LogInformation("Jelly Crowd deleted library item {Id} ({Name}).", id, item.Name);
    return true;
  }
}
