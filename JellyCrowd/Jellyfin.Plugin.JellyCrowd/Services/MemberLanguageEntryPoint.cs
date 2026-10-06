using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Readies <see cref="MemberLanguages"/> at startup: points it at the server's display language and loads
/// the languages members reported, so the first notification after a restart is already in theirs.
/// </summary>
public sealed class MemberLanguageEntryPoint : IHostedService
{
  private readonly IServerConfigurationManager _serverConfig;
  private readonly IUserPrefsStore _prefs;
  private readonly ILogger<MemberLanguageEntryPoint> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="MemberLanguageEntryPoint"/> class.
  /// </summary>
  /// <param name="serverConfig">The server configuration (its display language).</param>
  /// <param name="prefs">The preferences store (persists the members' languages).</param>
  /// <param name="logger">The logger.</param>
  public MemberLanguageEntryPoint(IServerConfigurationManager serverConfig, IUserPrefsStore prefs, ILogger<MemberLanguageEntryPoint> logger)
  {
    _serverConfig = serverConfig;
    _prefs = prefs;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task StartAsync(CancellationToken cancellationToken)
  {
    MemberLanguages.ServerLanguageSource = () => _serverConfig.Configuration.UICulture;
    try
    {
      // Any read loads the whole store, which hands every saved language to MemberLanguages.
      await _prefs.GetAsync(Guid.Empty, cancellationToken).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // Startup must not fail over it: notifications fall back to the server's language.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd could not load the members' languages; notifications use the server's until they are reported again.");
    }
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
