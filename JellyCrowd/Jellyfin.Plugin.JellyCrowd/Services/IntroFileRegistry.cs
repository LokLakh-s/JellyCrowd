using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IIntroFileRegistry"/>. Keeps a persisted path → item-id map so each pre-roll is
/// registered once (never duplicated) and can be pruned or re-created as files come and go — or after a
/// library scan removes the standalone items.
/// </summary>
public sealed class IntroFileRegistry : IIntroFileRegistry
{
  // Marks our standalone pre-roll items so they are identifiable and never confused with real content.
  private const string PrerollProviderId = "jellycrowd.preroll";

  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly Func<string> _filePathProvider;
  private readonly object _gate = new();
  private string? _filePath;
  private Dictionary<string, Guid>? _map;

  /// <summary>
  /// Initializes a new instance of the <see cref="IntroFileRegistry"/> class.
  /// </summary>
  /// <param name="filePathProvider">Resolves the backing file path lazily — this is resolved very early in
  /// startup, before <c>Plugin.Instance</c> exists, so the path must not be computed until first use.</param>
  public IntroFileRegistry(Func<string> filePathProvider)
  {
    _filePathProvider = filePathProvider;
  }

  private string FilePath => _filePath ??= _filePathProvider();

  /// <inheritdoc />
  public IReadOnlyList<Guid> EnsureAndGetIds(ILibraryManager libraryManager, string folderName)
  {
    ArgumentNullException.ThrowIfNull(libraryManager);

    // Discovery only ADDS newly-found pre-rolls; it never drives removal. Removal is keyed on the file
    // actually being gone (File.Exists), so dropping the legacy "Local Intros" library — which can stop the
    // folder being discovered — does not wrongly prune still-present pre-rolls.
    var discovered = LocalIntrosDiscovery.FindPrerollFiles(libraryManager, folderName);

    lock (_gate)
    {
      Load();
      var map = _map!;
      var changed = false;

      // Register any newly-discovered file that isn't tracked yet, or whose item a scan pruned.
      foreach (var path in discovered)
      {
        if (!map.TryGetValue(path, out var id) || libraryManager.GetItemById(id) is null)
        {
          map[path] = CreateItem(libraryManager, path);
          changed = true;
        }
      }

      // Drop entries whose file is gone (removing the stale db item; never the file itself), and re-create
      // any surviving pre-roll a library scan pruned so it stays playable.
      foreach (var path in map.Keys.ToList())
      {
        if (!File.Exists(path))
        {
          RemoveItem(libraryManager, map[path]);
          map.Remove(path);
          changed = true;
        }
        else if (libraryManager.GetItemById(map[path]) is null)
        {
          map[path] = CreateItem(libraryManager, path);
          changed = true;
        }
      }

      if (changed)
      {
        Save();
      }

      // Every remaining entry now has an existing file and a live item — return them in a stable order.
      return map.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
    }
  }

  private static Guid CreateItem(ILibraryManager libraryManager, string path)
  {
    // A standalone Video (no parent library): indexed so Cinema Mode can resolve and play it, but not part
    // of any browsable library — so users never see a "Local Intros" library, and per-library access can't
    // stop a restricted user from playing it.
    var video = new Video
    {
      Id = Guid.NewGuid(),
      Path = path,
      Name = Path.GetFileNameWithoutExtension(path),
      ProviderIds = new Dictionary<string, string> { [PrerollProviderId] = "1" }
    };

    libraryManager.CreateItem(video, null);
    return video.Id;
  }

  private static void RemoveItem(ILibraryManager libraryManager, Guid id)
  {
    var item = libraryManager.GetItemById(id);
    if (item is not null)
    {
      // Remove only the database entry — never the pre-roll file on disk.
      libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = false, DeleteFromExternalProvider = false });
    }
  }

  private void Load()
  {
    if (_map is not null)
    {
      return;
    }

    try
    {
      if (File.Exists(FilePath))
      {
        var json = File.ReadAllText(FilePath);
        _map = JsonSerializer.Deserialize<Dictionary<string, Guid>>(json, SerializerOptions);
      }
    }
#pragma warning disable CA1031 // A corrupt/unreadable registry is non-fatal: start empty and rebuild.
    catch (Exception)
#pragma warning restore CA1031
    {
      _map = null;
    }

    _map ??= new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
  }

  private void Save()
  {
    var path = FilePath;
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }

    // Write to a temp file then move, so an interrupted write can't corrupt the registry.
    var tmp = path + ".tmp";
    File.WriteAllText(tmp, JsonSerializer.Serialize(_map, SerializerOptions));
    File.Move(tmp, path, overwrite: true);
  }
}
