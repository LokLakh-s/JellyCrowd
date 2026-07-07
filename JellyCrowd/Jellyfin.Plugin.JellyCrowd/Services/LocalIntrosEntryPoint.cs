using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Keeps Local Intros "just working" from a folder: the pre-roll videos dropped in a named folder beside the
/// media are registered as standalone (library-less) items so Cinema Mode can play them — with no browsable
/// "Local Intros" library that would confuse end users. Runs at startup and whenever the configuration
/// changes, and migrates away the legacy auto-created library.
/// </summary>
public sealed class LocalIntrosEntryPoint : IHostedService
{
  // The library earlier versions auto-created for pre-rolls; now removed in favour of standalone items.
  private const string LegacyLibraryName = "Local Intros";

  private readonly ILibraryManager _libraryManager;
  private readonly IIntroFileRegistry _registry;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<LocalIntrosEntryPoint> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="LocalIntrosEntryPoint"/> class.
  /// </summary>
  /// <param name="libraryManager">The library manager.</param>
  /// <param name="registry">Registers the pre-roll files as standalone items.</param>
  /// <param name="config">Provides the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public LocalIntrosEntryPoint(ILibraryManager libraryManager, IIntroFileRegistry registry, Func<PluginConfiguration> config, ILogger<LocalIntrosEntryPoint> logger)
  {
    _libraryManager = libraryManager;
    _registry = registry;
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

    // Don't block startup: sync in the background.
    _ = Task.Run(SyncAsync, cancellationToken);
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

  private void OnConfigurationChanged(object? sender, BasePluginConfiguration e) => _ = Task.Run(SyncAsync);

  private async Task SyncAsync()
  {
    try
    {
      var config = _config();
      if (config.LocalIntrosEnabled)
      {
        // Register the pre-rolls as standalone items. Done BEFORE dropping the legacy library so, on a first
        // run, that library's location still helps discover the folder; afterwards the registry keys on file
        // existence, so removing it can't lose the pre-rolls.
        var ids = _registry.EnsureAndGetIds(_libraryManager, config.LocalIntrosFolderName);
        _logger.LogInformation("Jelly Crowd Local Intros: {Count} pre-roll(s) registered.", ids.Count);
      }

      await RemoveLegacyLibraryAsync().ConfigureAwait(false);
    }
#pragma warning disable CA1031 // Best-effort: a failure just means no pre-roll until the next run.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd Local Intros: could not sync the pre-rolls.");
    }
  }

  private async Task RemoveLegacyLibraryAsync()
  {
    if (_libraryManager.GetVirtualFolders().Any(v => string.Equals(v.Name, LegacyLibraryName, StringComparison.Ordinal)))
    {
      await _libraryManager.RemoveVirtualFolder(LegacyLibraryName, refreshLibrary: false).ConfigureAwait(false);
      _logger.LogInformation("Jelly Crowd Local Intros: removed the legacy '{Name}' library (pre-rolls now play library-less).", LegacyLibraryName);
    }
  }
}
