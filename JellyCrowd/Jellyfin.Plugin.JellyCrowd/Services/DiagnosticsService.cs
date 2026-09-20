using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IDiagnosticsService"/>: validates TMDB connectivity, the download backend, and
/// write access to the data folder, plus the store footprint.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
  private static readonly string[] StoreFiles = { "requests.json", "watchlist.json", "notifications.json", "user-prefs.json" };

  private readonly ITmdbClient _tmdb;
  private readonly IDownloadDispatcher _dispatcher;
  private readonly IServarrClient _servarr;
  private readonly IRequestStore _requests;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<DiagnosticsService> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="DiagnosticsService"/> class.
  /// </summary>
  /// <param name="tmdb">The TMDB client.</param>
  /// <param name="dispatcher">The download dispatcher (for the backend reachability test).</param>
  /// <param name="servarr">The Radarr/Sonarr client (for the indexer check).</param>
  /// <param name="requests">The request store (for the storage-growth estimate).</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public DiagnosticsService(ITmdbClient tmdb, IDownloadDispatcher dispatcher, IServarrClient servarr, IRequestStore requests, Func<PluginConfiguration> config, ILogger<DiagnosticsService> logger)
  {
    _tmdb = tmdb;
    _dispatcher = dispatcher;
    _servarr = servarr;
    _requests = requests;
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
      await CheckDownloadBackendAsync(config, cancellationToken).ConfigureAwait(false)
    };

    var indexers = await CheckIndexersAsync(config, cancellationToken).ConfigureAwait(false);
    if (indexers is not null)
    {
      results.Add(indexers);
    }

    results.Add(CheckSegmentCompanion(config));

    if (Plugin.Instance is { } plugin)
    {
      results.Add(CheckDataFolder(plugin.DataFolderPath));
      results.Add(Footprint(plugin.DataFolderPath));
      results.Add(await GrowthEstimateAsync(plugin.DataFolderPath, cancellationToken).ConfigureAwait(false));
    }

    return results;
  }

  // Reports which media-segment companion half is live. The companion ships twice — built against the
  // 10.11 SDK and against 12.x — and the plugin loads whichever one the running host can, swallowing the
  // other's load failure. That silence is deliberate (a mismatch must never take the plugin down) but it
  // also means a packaging slip would turn Skip Outro off with nothing to show for it. This surfaces it.
  private static DiagnosticResult CheckSegmentCompanion(PluginConfiguration config)
  {
    var loaded = PluginServiceRegistrator.CompanionStatus;
    var active = loaded.StartsWith("Loaded ", StringComparison.Ordinal);

    if (!config.SkipOutroEnabled && !config.SkipIntroEnabled)
    {
      return new DiagnosticResult
      {
        Name = "Media segments",
        Status = "info",
        Detail = "Skip Outro and Local Intros are turned off in the settings. " + loaded
      };
    }

    return new DiagnosticResult
    {
      Name = "Media segments",
      Status = active ? "ok" : "error",
      Detail = active
        ? loaded
        : loaded + " The feature is enabled in the settings but no compatible companion was found for this "
          + "Jellyfin version — reinstall the plugin, and check that the lib/ folder shipped with it."
    };
  }

  // Reports the enabled-indexer count for each configured *arr instance (Radarr and Sonarr) — these
  // are typically synced from Prowlarr, which is the upstream source but isn't queried directly here.
  private async Task<DiagnosticResult?> CheckIndexersAsync(PluginConfiguration config, CancellationToken cancellationToken)
  {
    if (!string.Equals(config.DownloadBackend, "servarr", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    // (Label, Url, Key, IsProwlarr) — Prowlarr is the upstream indexer manager on a different API version.
    var instances = new List<(string Label, string Url, string Key, bool IsProwlarr)>();
    if (!string.IsNullOrWhiteSpace(config.ProwlarrUrl) && !string.IsNullOrWhiteSpace(config.ProwlarrApiKey))
    {
      instances.Add(("Prowlarr", config.ProwlarrUrl, config.ProwlarrApiKey, true));
    }

    if (!string.IsNullOrWhiteSpace(config.RadarrUrl) && !string.IsNullOrWhiteSpace(config.RadarrApiKey))
    {
      instances.Add(("Radarr", config.RadarrUrl, config.RadarrApiKey, false));
    }

    if (!string.IsNullOrWhiteSpace(config.SonarrUrl) && !string.IsNullOrWhiteSpace(config.SonarrApiKey))
    {
      instances.Add(("Sonarr", config.SonarrUrl, config.SonarrApiKey, false));
    }

    if (instances.Count == 0)
    {
      return null;
    }

    var parts = new List<string>();
    var worst = "ok";
    foreach (var (label, baseUrl, apiKey, isProwlarr) in instances)
    {
      try
      {
        var json = isProwlarr
          ? await _servarr.GetProwlarrIndexersAsync(baseUrl, apiKey, cancellationToken).ConfigureAwait(false)
          : await _servarr.GetIndexersAsync(baseUrl, apiKey, cancellationToken).ConfigureAwait(false);
        var enabled = ServarrIndexerParser.CountEnabled(json);
        parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} enabled", label, enabled));
        if (enabled == 0)
        {
          worst = Worse(worst, "warning");
        }
      }
#pragma warning disable CA1031 // Diagnostics must report any failure, not throw.
      catch (Exception ex)
#pragma warning restore CA1031
      {
        _logger.LogDebug(ex, "Indexer diagnostic failed for {Label}.", label);
        parts.Add(label + ": error (" + ex.Message + ")");
        worst = Worse(worst, "error");
      }
    }

    var detail = string.Join(" · ", parts);
    if (string.IsNullOrWhiteSpace(config.ProwlarrUrl))
    {
      detail += " (indexers are managed in Prowlarr and synced here)";
    }

    if (worst == "warning")
    {
      detail += " — an instance has no enabled indexer; its searches find nothing.";
    }

    return Result("Indexers", worst, detail);
  }

  // Returns the more severe of two statuses (error > warning > ok).
  private static string Worse(string a, string b)
  {
    static int Rank(string s) => string.Equals(s, "error", StringComparison.Ordinal) ? 2 : string.Equals(s, "warning", StringComparison.Ordinal) ? 1 : 0;
    return Rank(b) > Rank(a) ? b : a;
  }

  // Rough projection: bytes/request from the requests store, applied to the last 30 days' volume,
  // plus a per-user average. Clearly an estimate, to size capacity planning (the "growth" rule).
  private async Task<DiagnosticResult> GrowthEstimateAsync(string dir, CancellationToken cancellationToken)
  {
    try
    {
      var all = await _requests.GetAllAsync(cancellationToken).ConfigureAwait(false);
      var requestsBytes = FileSize(Path.Combine(dir, "requests.json"));
      long totalBytes = 0;
      foreach (var file in StoreFiles)
      {
        totalBytes += FileSize(Path.Combine(dir, file));
      }

      var users = all.Select(r => r.UserId).Distinct().Count();
      var perUser = totalBytes / Math.Max(1, users);
      var bytesPerRequest = all.Count > 0 ? requestsBytes / all.Count : 0;
      var cutoff = DateTime.UtcNow.AddDays(-30);
      var recent = all.Count(r => r.RequestedAt >= cutoff);
      var monthly = recent * bytesPerRequest;

      return Result(
        "Storage growth",
        "info",
        string.Format(
          CultureInfo.InvariantCulture,
          "~{0}/user ({1} users) · ~{2}/month (based on {3} requests in the last 30 days). Stores are bounded by caps + retention.",
          FormatBytes(perUser),
          users,
          FormatBytes(monthly),
          recent));
    }
#pragma warning disable CA1031 // Diagnostics must report any failure, not throw.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Growth estimate failed.");
      return Result("Storage growth", "info", "Estimate unavailable.");
    }
  }

  private static long FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

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
