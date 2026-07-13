using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Reads and writes a JSON store file as a versioned <see cref="StoreEnvelope{T}"/>, atomically. Reads
/// are backward-compatible with the legacy bare-array format (treated as schema version 0) and run an
/// optional migration when the stored version is older than the current one. Network-free and shared
/// by every JSON store so versioning/migration lives in one place.
/// </summary>
internal static class VersionedJsonFile
{
  /// <summary>
  /// Reads the items from <paramref name="path"/>. Accepts both the enveloped and the legacy bare-array
  /// formats; applies <paramref name="migrate"/> when the stored version is below <paramref name="currentVersion"/>.
  /// </summary>
  /// <typeparam name="T">The item type.</typeparam>
  /// <param name="path">The file path.</param>
  /// <param name="currentVersion">The current schema version.</param>
  /// <param name="migrate">Optional migration: <c>(storedVersion, items) =&gt; migratedItems</c>.</param>
  /// <param name="options">The serializer options.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The items (empty list when the file is missing or empty).</returns>
  public static async Task<List<T>> ReadAsync<T>(
    string path,
    int currentVersion,
    Func<int, List<T>, List<T>>? migrate,
    JsonSerializerOptions options,
    CancellationToken cancellationToken)
  {
    if (!File.Exists(path))
    {
      return new List<T>();
    }

    var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    if (string.IsNullOrWhiteSpace(json))
    {
      return new List<T>();
    }

    int storedVersion;
    List<T> items;
    try
    {
      if (json.TrimStart().StartsWith('['))
      {
        // Legacy format: a bare array, written before versioning existed.
        storedVersion = 0;
        items = JsonSerializer.Deserialize<List<T>>(json, options) ?? new List<T>();
      }
      else
      {
        var envelope = JsonSerializer.Deserialize<StoreEnvelope<T>>(json, options) ?? new StoreEnvelope<T>();
        storedVersion = envelope.SchemaVersion;
        items = envelope.Items ?? new List<T>();
      }
    }
    catch (JsonException)
    {
      // A corrupt file (partial write from a crash, external tampering) must not brick the store: every
      // read would otherwise throw and the endpoint would 500 forever. Quarantine it so the bytes can be
      // inspected, then fall back to the copy taken before the last write — for the request store, that
      // is every user's media ownership and quota, which nothing else in Jellyfin knows.
      Quarantine(path);
      return await ReadBackupAsync<T>(path, currentVersion, migrate, options, cancellationToken).ConfigureAwait(false);
    }

    if (migrate is not null && storedVersion < currentVersion)
    {
      items = migrate(storedVersion, items) ?? items;
    }

    return items;
  }

  /// <summary>
  /// Writes <paramref name="items"/> to <paramref name="path"/> as a <see cref="StoreEnvelope{T}"/>
  /// stamped with <paramref name="currentVersion"/>, atomically (temp file + move).
  /// </summary>
  /// <typeparam name="T">The item type.</typeparam>
  /// <param name="path">The file path.</param>
  /// <param name="currentVersion">The current schema version to stamp.</param>
  /// <param name="items">The items to persist.</param>
  /// <param name="options">The serializer options.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the file has been written.</returns>
  public static async Task WriteAsync<T>(
    string path,
    int currentVersion,
    List<T> items,
    JsonSerializerOptions options,
    CancellationToken cancellationToken)
  {
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }

    var envelope = new StoreEnvelope<T> { SchemaVersion = currentVersion, Items = items ?? new List<T>() };
    var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, options);
    var tempPath = path + ".tmp";
    await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

    // Keep the version we are about to replace. The move below is atomic, but the bytes it replaces are
    // the only copy that exists — a bad write, a truncated file or a bug would otherwise be unrecoverable.
    Backup(path);
    File.Move(tempPath, path, overwrite: true);
  }

  // Best-effort copy of the current file, kept as the one-deep undo of the next write.
  private static void Backup(string path)
  {
    try
    {
      if (File.Exists(path))
      {
        File.Copy(path, path + ".bak", overwrite: true);
      }
    }
#pragma warning disable CA1031 // Backing up must never break a write.
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
#pragma warning restore CA1031
  }

  // Re-read the pre-write copy after the live file turned out to be corrupt. Empty when there is none (or
  // it is corrupt too), which leaves the store empty rather than throwing.
  private static async Task<List<T>> ReadBackupAsync<T>(
    string path,
    int currentVersion,
    Func<int, List<T>, List<T>>? migrate,
    JsonSerializerOptions options,
    CancellationToken cancellationToken)
  {
    var backup = path + ".bak";
    if (!File.Exists(backup))
    {
      return new List<T>();
    }

    try
    {
      var items = await ReadAsync<T>(backup, currentVersion, migrate, options, cancellationToken).ConfigureAwait(false);

      // Reinstate it as the live file, so the recovered data is what the next write builds on.
      File.Copy(backup, path, overwrite: true);
      return items;
    }
#pragma warning disable CA1031 // Recovery is best-effort; an unusable backup just yields an empty store.
    catch (Exception)
#pragma warning restore CA1031
    {
      return new List<T>();
    }
  }

  // Move a corrupt store aside (best-effort) so it is not re-read on every call, keeping the bytes for
  // forensics/recovery rather than silently discarding them.
  private static void Quarantine(string path)
  {
    try
    {
      File.Move(path, path + ".corrupt", overwrite: true);
    }
#pragma warning disable CA1031 // Recovery must never throw — the caller falls back to an empty store.
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
#pragma warning restore CA1031
  }
}
