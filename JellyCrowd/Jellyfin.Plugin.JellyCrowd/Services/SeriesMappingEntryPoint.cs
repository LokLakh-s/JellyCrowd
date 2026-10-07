using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Loads the TMDB → Sonarr series mappings at startup, so the library lookups that rely on them find a mapped
/// show from the first request on.
/// </summary>
public sealed class SeriesMappingEntryPoint : IHostedService
{
  private readonly ISeriesMappingStore _mappings;
  private readonly ILogger<SeriesMappingEntryPoint> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="SeriesMappingEntryPoint"/> class.
  /// </summary>
  /// <param name="mappings">The mapping store.</param>
  /// <param name="logger">The logger.</param>
  public SeriesMappingEntryPoint(ISeriesMappingStore mappings, ILogger<SeriesMappingEntryPoint> logger)
  {
    _mappings = mappings;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      await _mappings.LoadAsync(cancellationToken).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // Startup must not fail over it: a mapped show is then rediscovered on its next dispatch.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Jelly Crowd could not load its series mappings.");
    }
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
