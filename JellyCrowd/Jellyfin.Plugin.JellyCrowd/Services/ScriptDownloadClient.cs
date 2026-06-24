using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Download backend that runs a local script, passing the request as JSON on stdin plus
/// <c>JELLYCROWD_*</c> environment variables. Lets the admin wire any custom download flow
/// (qBittorrent, SABnzbd, …) — Jelly Crowd only emits the request, it does not search/download.
/// </summary>
public sealed class ScriptDownloadClient : IDownloadClient
{
  private readonly IProcessRunner _processRunner;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="ScriptDownloadClient"/> class.
  /// </summary>
  /// <param name="processRunner">The process runner.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  public ScriptDownloadClient(IProcessRunner processRunner, Func<PluginConfiguration> config)
  {
    _processRunner = processRunner;
    _config = config;
  }

  /// <inheritdoc />
  public string Backend => "script";

  /// <inheritdoc />
  public bool IsConfigured(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return !string.IsNullOrWhiteSpace(config.ScriptPath);
  }

  /// <inheritdoc />
  public Task DispatchAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    return RunAsync(dispatch, cancellationToken);
  }

  /// <inheritdoc />
  public Task TestAsync(CancellationToken cancellationToken)
  {
    var sample = new DownloadDispatch
    {
      RequestId = Guid.Empty,
      UserName = "Jelly Crowd test",
      TmdbId = 603,
      MediaType = "movie",
      Title = "The Matrix",
      Year = 1999,
      ReleaseDate = "1999-03-30",
      RequestedAt = DateTime.UtcNow,
      TmdbUrl = "https://www.themoviedb.org/movie/603"
    };
    return RunAsync(sample, cancellationToken);
  }

  /// <inheritdoc />
  public Task CancelAsync(DownloadDispatch dispatch, CancellationToken cancellationToken) => Task.CompletedTask;

  /// <inheritdoc />
  public Task PurgeAsync(DownloadDispatch dispatch, CancellationToken cancellationToken) => Task.CompletedTask;

  /// <inheritdoc />
  public Task RetryAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(dispatch);
    return RunAsync(dispatch, cancellationToken);
  }

  private Task RunAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
  {
    var config = _config();
    if (!IsConfigured(config))
    {
      throw new InvalidOperationException("The download script path is not configured.");
    }

    var json = JsonSerializer.Serialize(dispatch);
    var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
      ["JELLYCROWD_TMDBID"] = dispatch.TmdbId.ToString(CultureInfo.InvariantCulture),
      ["JELLYCROWD_MEDIATYPE"] = dispatch.MediaType,
      ["JELLYCROWD_TITLE"] = dispatch.Title,
      ["JELLYCROWD_YEAR"] = dispatch.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
      ["JELLYCROWD_SEASON"] = dispatch.Season?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
      ["JELLYCROWD_EPISODE"] = dispatch.Episode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
      ["JELLYCROWD_USER"] = dispatch.UserName
    };

    return _processRunner.RunAsync(config.ScriptPath, config.ScriptArguments, json, environment, cancellationToken);
  }
}
