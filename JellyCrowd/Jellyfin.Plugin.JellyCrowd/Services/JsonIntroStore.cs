using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IIntroStore"/> persisting the episode-id → intro map as JSON in the plugin data
/// folder. Reads are served from an in-memory cache; writes are infrequent (one per season per analysis).
/// </summary>
public sealed class JsonIntroStore : IIntroStore
{
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly Func<string> _filePathProvider;
  private readonly object _gate = new();
  private string? _filePath;
  private Dictionary<Guid, IntroSegment>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonIntroStore"/> class.
  /// </summary>
  /// <param name="filePathProvider">Resolves the backing file path lazily — the provider (which depends on
  /// this store) is resolved very early in startup, before <c>Plugin.Instance</c> exists, so the path must
  /// not be computed until first use.</param>
  public JsonIntroStore(Func<string> filePathProvider)
  {
    _filePathProvider = filePathProvider;
  }

  private string FilePath => _filePath ??= _filePathProvider();

  /// <inheritdoc />
  public IntroSegment? Get(Guid itemId)
  {
    lock (_gate)
    {
      Load();
      return _cache!.TryGetValue(itemId, out var segment) ? segment : null;
    }
  }

  /// <inheritdoc />
  public void UpsertSeason(IReadOnlyDictionary<Guid, IntroSegment?> seasonIntros)
  {
    ArgumentNullException.ThrowIfNull(seasonIntros);

    lock (_gate)
    {
      Load();
      foreach (var (id, segment) in seasonIntros)
      {
        if (segment is null)
        {
          _cache!.Remove(id);
        }
        else
        {
          _cache![id] = segment;
        }
      }

      Save();
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
        _cache = JsonSerializer.Deserialize<Dictionary<Guid, IntroSegment>>(json, SerializerOptions);
      }
    }
#pragma warning disable CA1031 // A corrupt/unreadable cache is non-fatal: start empty and rebuild on next analysis.
    catch (Exception)
#pragma warning restore CA1031
    {
      _cache = null;
    }

    _cache ??= new Dictionary<Guid, IntroSegment>();
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
