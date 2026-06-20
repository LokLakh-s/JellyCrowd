using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IDiagnosticsService"/>: validates TMDB connectivity, the File Transformation
/// dependency, the download backend, and write access to the data folder, plus the store footprint.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
  private static readonly string[] StoreFiles = { "requests.json", "watchlist.json", "notifications.json", "user-prefs.json" };

  private readonly ITmdbClient _tmdb;
  private readonly IDownloadDispatcher _dispatcher;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<DiagnosticsService> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="DiagnosticsService"/> class.
  /// </summary>
  /// <param name="tmdb">The TMDB client.</param>
  /// <param name="dispatcher">The download dispatcher (for the backend reachability test).</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public DiagnosticsService(ITmdbClient tmdb, IDownloadDispatcher dispatcher, Func<PluginConfiguration> config, ILogger<DiagnosticsService> logger)
  {
    _tmdb = tmdb;
    _dispatcher = dispatcher;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(CancellationToken cancellationToken)
  {
    var config = _config();
    var results = new List<DiagnosticResult>
    {
      await CheckTmdbAsync(config, cancellationToken).ConfigureAwait(false),
      CheckFileTransformation(),
      await CheckDownloadBackendAsync(config, cancellationToken).ConfigureAwait(false)
    };

    if (Plugin.Instance is { } plugin)
    {
      results.Add(CheckDataFolder(plugin.DataFolderPath));
      results.Add(Footprint(plugin.DataFolderPath));
    }

    return results;
  }

  private static DiagnosticResult Result(string name, string status, string detail) => new() { Name = name, Status = status, Detail = detail };

  private async Task<DiagnosticResult> CheckTmdbAsync(PluginConfiguration config, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(config.TmdbApiKey))
    {
      return Result("TMDB", "error", "No API key set — the catalog cannot load. Set it in Settings.");
    }

    try
    {
      var trending = await _tmdb.GetTrendingAsync("en-US", cancellationToken).ConfigureAwait(false);
      return trending is { Count: > 0 }
        ? Result("TMDB", "ok", "Connected (fetched trending titles).")
        : Result("TMDB", "warning", "Connected, but no results were returned.");
    }
#pragma warning disable CA1031 // Diagnostics must report any failure, not throw.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "TMDB diagnostic failed.");
      return Result("TMDB", "error", "TMDB request failed: " + ex.Message);
    }
  }

  private static DiagnosticResult CheckFileTransformation()
  {
    var present = AssemblyLoadContext.All
      .SelectMany(context => context.Assemblies)
      .Any(a => a.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) == true);
    return present
      ? Result("File Transformation", "ok", "Detected — the Jelly Crowd header and pages are injected.")
      : Result("File Transformation", "warning", "Not detected — install the File Transformation plugin so the UI loads.");
  }

  private async Task<DiagnosticResult> CheckDownloadBackendAsync(PluginConfiguration config, CancellationToken cancellationToken)
  {
    var backend = config.DownloadBackend;
    if (string.IsNullOrWhiteSpace(backend) || string.Equals(backend, "none", StringComparison.OrdinalIgnoreCase))
    {
      return Result("Download backend", "info", "None configured — approved requests are not dispatched anywhere.");
    }

    try
    {
      await _dispatcher.TestActiveAsync(cancellationToken).ConfigureAwait(false);
      return Result("Download backend", "ok", backend + ": reachable.");
    }
#pragma warning disable CA1031 // Diagnostics must report any failure, not throw.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Download backend diagnostic failed.");
      return Result("Download backend", "error", backend + ": " + ex.Message);
    }
  }

  private DiagnosticResult CheckDataFolder(string dir)
  {
    try
    {
      Directory.CreateDirectory(dir);
      var probe = Path.Combine(dir, ".jc-write-probe");
      File.WriteAllText(probe, "ok");
      File.Delete(probe);
      return Result("Data folder", "ok", "Writable: " + dir);
    }
#pragma warning disable CA1031 // Diagnostics must report any failure, not throw.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Data folder write probe failed.");
      return Result("Data folder", "error", "Not writable (" + dir + "): " + ex.Message);
    }
  }

  private static DiagnosticResult Footprint(string dir)
  {
    long total = 0;
    var parts = new List<string>();
    foreach (var file in StoreFiles)
    {
      var path = Path.Combine(dir, file);
      var size = File.Exists(path) ? new FileInfo(path).Length : 0;
      total += size;
      parts.Add(file + " " + FormatBytes(size));
    }

    return Result("Storage footprint", "info", string.Join(" · ", parts) + " · total " + FormatBytes(total));
  }

  private static string FormatBytes(long bytes)
  {
    string[] units = { "B", "KiB", "MiB", "GiB" };
    double value = bytes;
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1)
    {
      value /= 1024;
      unit++;
    }

    return (unit == 0 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.0", CultureInfo.InvariantCulture)) + " " + units[unit];
  }
}
