using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IDownloadDispatcher"/>: resolves the active backend from configuration and
/// hands due requests to it, stamping <see cref="RequestRecord.DispatchedAt"/> on success so a
/// request is never dispatched twice. Failures are logged and left undispatched for the next retry.
/// </summary>
public sealed class DownloadDispatcher : IDownloadDispatcher
{
  private readonly IReadOnlyList<IDownloadClient> _clients;
  private readonly IRequestStore _store;
  private readonly Func<Guid, string> _resolveUserName;
  private readonly Func<PluginConfiguration> _config;
  private readonly ILogger<DownloadDispatcher> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="DownloadDispatcher"/> class.
  /// </summary>
  /// <param name="clients">The available download backends.</param>
  /// <param name="store">The request store.</param>
  /// <param name="resolveUserName">Resolves a user id to a display name.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="logger">The logger.</param>
  public DownloadDispatcher(
    IEnumerable<IDownloadClient> clients,
    IRequestStore store,
    Func<Guid, string> resolveUserName,
    Func<PluginConfiguration> config,
    ILogger<DownloadDispatcher> logger)
  {
    ArgumentNullException.ThrowIfNull(clients);
    _clients = clients.ToList();
    _store = store;
    _resolveUserName = resolveUserName;
    _config = config;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);

    var now = DateTime.UtcNow;
    if (!DownloadEligibility.IsDue(request, now))
    {
      return false;
    }

    var client = ActiveClient(_config());
    if (client is null)
    {
      return false;
    }

    return await DispatchOneAsync(request, client, now, cancellationToken).ConfigureAwait(false);
  }

  /// <inheritdoc />
  public async Task DispatchDueAsync(CancellationToken cancellationToken)
  {
    var client = ActiveClient(_config());
    if (client is null)
    {
      return;
    }

    var now = DateTime.UtcNow;
    var due = await _store.GetDueForDispatchAsync(now, cancellationToken).ConfigureAwait(false);
    foreach (var request in due)
    {
      await DispatchOneAsync(request, client, now, cancellationToken).ConfigureAwait(false);
    }
  }

  /// <inheritdoc />
  public Task TestActiveAsync(CancellationToken cancellationToken)
  {
    var config = _config();
    if (IsNoneBackend(config.DownloadBackend))
    {
      throw new InvalidOperationException("No download backend is selected.");
    }

    var client = _clients.FirstOrDefault(c => string.Equals(c.Backend, config.DownloadBackend, StringComparison.OrdinalIgnoreCase))
      ?? throw new InvalidOperationException("The selected download backend is not available.");

    return client.TestAsync(cancellationToken);
  }

  /// <inheritdoc />
  public async Task CancelAsync(RequestRecord request, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);
    var client = ActiveClient(_config());
    if (client is null)
    {
      return;
    }

    try
    {
      var name = _resolveUserName(request.UserId);
      var payload = DownloadPayloadBuilder.Build(request, name);
      await client.CancelAsync(payload, cancellationToken).ConfigureAwait(false);
      _logger.LogInformation(
        "Requested upstream cancel of request {RequestId} on the {Backend} backend.",
        request.Id.ToString("N", CultureInfo.InvariantCulture),
        client.Backend);
    }
#pragma warning disable CA1031 // Upstream cancel is best-effort; the local cancel still proceeds.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Upstream cancel failed for request {RequestId}.", request.Id.ToString("N", CultureInfo.InvariantCulture));
    }
  }

  private static bool IsNoneBackend(string? backend)
    => string.IsNullOrWhiteSpace(backend) || string.Equals(backend, "none", StringComparison.OrdinalIgnoreCase);

  private IDownloadClient? ActiveClient(PluginConfiguration config)
  {
    if (IsNoneBackend(config.DownloadBackend))
    {
      return null;
    }

    return _clients.FirstOrDefault(c =>
      string.Equals(c.Backend, config.DownloadBackend, StringComparison.OrdinalIgnoreCase) && c.IsConfigured(config));
  }

  private async Task<bool> DispatchOneAsync(RequestRecord request, IDownloadClient client, DateTime nowUtc, CancellationToken cancellationToken)
  {
    try
    {
      var name = _resolveUserName(request.UserId);
      var payload = DownloadPayloadBuilder.Build(request, name);
      await client.DispatchAsync(payload, cancellationToken).ConfigureAwait(false);
      await _store.MarkDispatchedAsync(request.Id, nowUtc, cancellationToken).ConfigureAwait(false);
      _logger.LogInformation(
        "Dispatched request {RequestId} ({Title}) to the {Backend} download backend.",
        request.Id.ToString("N", CultureInfo.InvariantCulture),
        request.Title,
        client.Backend);
      return true;
    }
#pragma warning disable CA1031 // A backend failure must not break the request flow; it is retried by the scheduled task.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(
        ex,
        "Failed to dispatch request {RequestId} to the {Backend} download backend.",
        request.Id.ToString("N", CultureInfo.InvariantCulture),
        client.Backend);

      // Persist the reason so the admin can see why nothing reached the backend (the exception
      // otherwise only lands in the Jellyfin log). Truncated to keep the store small.
      var message = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
      await _store.SetDispatchErrorAsync(request.Id, message, nowUtc, cancellationToken).ConfigureAwait(false);
      return false;
    }
  }
}
