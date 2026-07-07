using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Registers local pre-roll videos as standalone (library-less) Jellyfin items so Cinema Mode can play them,
/// without creating a browsable "Local Intros" library that would confuse end users. Mirrors the approach of
/// the official Local Intros plugin: each pre-roll is inserted straight into the item database with no parent
/// library, which also sidesteps per-library access (a restricted user can still play it).
/// </summary>
public interface IIntroFileRegistry
{
  /// <summary>
  /// Ensures every pre-roll video found beside the media libraries is registered as a standalone item and
  /// returns their item ids. Re-creates any item a library scan pruned, and removes any whose file is gone
  /// (the file itself is never deleted).
  /// </summary>
  /// <param name="libraryManager">The library manager (finds the pre-roll folders and owns the items).</param>
  /// <param name="folderName">The pre-roll folder name to look for beside the libraries.</param>
  /// <returns>The pre-roll item ids currently backed by a file on disk.</returns>
  IReadOnlyList<Guid> EnsureAndGetIds(ILibraryManager libraryManager, string folderName);
}
