using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IOutroStore"/> persisting the item-id → outro-analysis map as JSON in the plugin
/// data folder. Reads are served from an in-memory cache; a write happens once per item as it is analyzed.
/// </summary>
public sealed class JsonOutroStore : IOutroStore
{
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly Func<string> _filePathProvider;
  private readonly object _gate = new();
  private string? _filePath;
  private Dictionary<Guid, OutroAnalysis>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonOutroStore"/> class.
  /// </summary>
  /// <param name="filePathProvider">Resolves the backing file path lazily — the segment provider (which
  /// depends on this store) is resolved very early in startup, before <c>Plugin.Instance</c> exists, so the
  /// path must not be computed until first use.</param>
  public JsonOutroStore(Func<string> filePathProvider)
  {
    _filePathProvider = filePathProvider;
  }

  private string FilePath => _filePath ??= _filePathProvider();

  /// <inheritdoc />
  public OutroAnalysis? Get(Guid itemId)
  {
    lock (_gate)
    {
      Load();
      return _cache!.TryGetValue(itemId, out var analysis) ? analysis : null;
    }
  }

  /// <inheritdoc />
  public void Set(Guid itemId, OutroAnalysis analysis)
  {
    ArgumentNullException.ThrowIfNull(analysis);

    lock (_gate)
    {
      Load();
      _cache![itemId] = analysis;
      Save();
    }
  }

  /// <inheritdoc />
  public void Remove(Guid itemId)
  {
    lock (_gate)
    {
      Load();
      if (_cache!.Remove(itemId))
      {
        Save();
      }
    }
  }

  private void Load()
  {
    if (_cache is not null)
    {
      return;
    }

    try
    {
      if (File.Exists(FilePath))
      {
        var json = File.ReadAllText(FilePath);
        _cache = JsonSerializer.Deserialize<Dictionary<Guid, OutroAnalysis>>(json, SerializerOptions);
      }
    }
#pragma warning disable CA1031 // A corrupt/unreadable cache is non-fatal: start empty and rebuild on next scan.
    catch (Exception)
#pragma warning restore CA1031
    {
      _cache = null;
    }

    _cache ??= new Dictionary<Guid, OutroAnalysis>();
  }

  private void Save()
  {
    var path = FilePath;
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }

    // Write to a temp file then move, so an interrupted write can't corrupt the cache.
    var tmp = path + ".tmp";
    File.WriteAllText(tmp, JsonSerializer.Serialize(_cache, SerializerOptions));
    File.Move(tmp, path, overwrite: true);
  }
}
