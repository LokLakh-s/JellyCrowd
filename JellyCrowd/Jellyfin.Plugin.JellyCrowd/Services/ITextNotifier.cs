using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// A simple text notification channel (title + body), in addition to the rich Discord/SMTP channels.
/// </summary>
public interface ITextNotifier
{
  /// <summary>
  /// Gets the channel identifier (e.g. <c>telegram</c>, <c>ntfy</c>, <c>slack</c>), used by the test endpoint.
  /// </summary>
  string Channel { get; }

  /// <summary>
  /// Determines whether the channel is configured.
  /// </summary>
  /// <param name="config">The current plugin configuration.</param>
  /// <returns><c>true</c> when the channel can be used.</returns>
  bool IsConfigured(PluginConfiguration config);

  /// <summary>
  /// Sends a title/body notification. Throws on failure.
  /// </summary>
  /// <param name="config">The current plugin configuration.</param>
  /// <param name="title">The notification title.</param>
  /// <param name="body">The notification body.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when delivery succeeds.</returns>
  Task SendAsync(PluginConfiguration config, string title, string body, CancellationToken cancellationToken);
}
