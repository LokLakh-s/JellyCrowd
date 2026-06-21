using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Records an activity-log entry whenever an administrator saves the plugin configuration, so admin
/// settings changes are visible in the Logs tab (previously nothing was logged for the "admin" category).
/// </summary>
public sealed class ConfigChangeLogger : IHostedService
{
  private readonly IActivityLog _activityLog;

  /// <summary>
  /// Initializes a new instance of the <see cref="ConfigChangeLogger"/> class.
  /// </summary>
  /// <param name="activityLog">The activity log.</param>
  public ConfigChangeLogger(IActivityLog activityLog) => _activityLog = activityLog;

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken)
  {
    if (Plugin.Instance is { } plugin)
    {
      plugin.ConfigurationChanged += OnConfigurationChanged;
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken)
  {
    if (Plugin.Instance is { } plugin)
    {
      plugin.ConfigurationChanged -= OnConfigurationChanged;
    }

    return Task.CompletedTask;
  }

  private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    => _ = _activityLog.LogAsync("info", "admin", "Plugin configuration saved", CancellationToken.None);
}
