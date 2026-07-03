using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Makes Local Intros "just work" from a folder: it finds the pre-roll folder beside the media libraries
/// and indexes it by creating a dedicated <c>Local Intros</c> library, so the pre-roll videos become
/// playable items (a bare, unscanned file is silently ignored by the player). Runs at startup and whenever
/// the plugin configuration changes.
/// </summary>
public sealed class LocalIntrosEntryPoint : IHostedService
{
  private const string LibraryName = "Local Intros";

  private readonly ILibraryManager _libraryManager;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<LocalIntrosEntryPoint> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="LocalIntrosEntryPoint"/> class.
  /// </summary>
  /// <param name="libraryManager">The library manager.</param>
  /// <param name="config">Provides the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public LocalIntrosEntryPoint(ILibraryManager libraryManager, Func<PluginConfiguration> config, ILogger<LocalIntrosEntryPoint> logger)
  {
    _libraryManager = libraryManager;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken)
  {
    if (Plugin.Instance is not null)
    {
      Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
    }

    // Don't block startup: ensure the library in the background.
    _ = Task.Run(EnsureLibraryAsync, cancellationToken);
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken)
  {
    if (Plugin.Instance is not null)
    {
      Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
    }

    return Task.CompletedTask;
  }

  private void OnConfigurationChanged(object? sender, BasePluginConfiguration e) => _ = Task.Run(EnsureLibraryAsync);

  private async Task EnsureLibraryAsync()
  {
    try
    {
      var config = _config();
      if (!config.LocalIntrosEnabled)
      {
        return;
      }

      var folders = LocalIntrosDiscovery.FindFolders(_libraryManager, config.LocalIntrosFolderName);
      if (folders.Count == 0)
      {
        _logger.LogInformation(
          "Jelly Crowd Local Intros: no '{Name}' folder found beside your libraries — create one and drop pre-roll videos in it.",
          config.LocalIntrosFolderName);
        return;
      }

      var existing = _libraryManager.GetVirtualFolders();
      var covered = new HashSet<string>(
        existing.SelectMany(v => v.Locations).Select(NormalizePath),
        StringComparer.OrdinalIgnoreCase);
      var uncovered = folders.Where(f => !covered.Contains(NormalizePath(f))).ToList();
      if (uncovered.Count == 0)
      {
        return;
      }

      var ourLibrary = existing.FirstOrDefault(v => string.Equals(v.Name, LibraryName, StringComparison.Ordinal));
      if (ourLibrary is null)
      {
        var options = new LibraryOptions
        {
          PathInfos = uncovered.Select(f => new MediaPathInfo { Path = f }).ToArray(),
        };
        await _libraryManager.AddVirtualFolder(LibraryName, null, options, refreshLibrary: true).ConfigureAwait(false);
      }
      else
      {
        foreach (var folder in uncovered)
        {
          _libraryManager.AddMediaPath(LibraryName, new MediaPathInfo { Path = folder });
        }
      }

      _logger.LogInformation("Jelly Crowd Local Intros: indexed {Count} pre-roll folder(s): {Folders}.", uncovered.Count, string.Join(", ", uncovered));
    }
#pragma warning disable CA1031 // Best-effort: a failure just means no pre-roll until the next run.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd Local Intros: could not ensure the pre-roll library.");
    }
  }

  private static string NormalizePath(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
