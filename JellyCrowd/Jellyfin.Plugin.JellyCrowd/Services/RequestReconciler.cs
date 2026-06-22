using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IRequestReconciler"/>: marks approved requests available (and notifies) once
/// the matching title is found in the Jellyfin library, recording the library item id.
/// </summary>
public sealed class RequestReconciler : IRequestReconciler
{
  private readonly IRequestStore _store;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly INotificationService _notificationService;
  private readonly ILogger<RequestReconciler> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="RequestReconciler"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="libraryMatcher">The library matcher.</param>
  /// <param name="notificationService">The notification service.</param>
  /// <param name="logger">The logger.</param>
  public RequestReconciler(IRequestStore store, ILibraryMatcher libraryMatcher, INotificationService notificationService, ILogger<RequestReconciler> logger)
  {
    _store = store;
    _libraryMatcher = libraryMatcher;
    _notificationService = notificationService;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
  {
    var all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    var resolved = 0;
    var reverted = 0;

    foreach (var request in all)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (request.Status == RequestStatus.Approved)
      {
        var itemId = ResolveItemId(request);
        if (itemId is not null)
        {
          await _store.MarkAvailableAsync(request.Id, itemId, cancellationToken).ConfigureAwait(false);
          await _notificationService.NotifyRequestEventAsync(request, NotificationEvent.Available, cancellationToken).ConfigureAwait(false);
          resolved++;
        }
      }
      else if (request.Status == RequestStatus.Available
               && request.DeletionRequestedAt is null
               && ResolveItemId(request) is null)
      {
        // The media is gone (e.g. deleted by another user / removed externally): no longer available.
        await _store.UpdateStatusAsync(request.Id, RequestStatus.Approved, request.DecidedBy ?? Guid.Empty, cancellationToken).ConfigureAwait(false);
        reverted++;
      }
    }

    if (resolved > 0 || reverted > 0)
    {
      _logger.LogInformation("Jelly Crowd reconcile: {Resolved} available, {Reverted} reverted.", resolved, reverted);
    }

    return resolved;
  }

  // For a per-episode / per-season TV request, availability means that exact episode (or season) is in
  // the library — not merely the series. Whole-show and movie requests match at the title level.
  private string? ResolveItemId(RequestRecord request)
  {
    if (string.Equals(request.MediaType, "tv", StringComparison.Ordinal) && request.Season.HasValue)
    {
      return _libraryMatcher.FindEpisodeItemId(request.TmdbId, request.Season, request.Episode);
    }

    return _libraryMatcher.FindItemId(request.MediaType, request.TmdbId);
  }
}
