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
  private readonly IDownloadDispatcher _dispatcher;
  private readonly ILogger<RequestReconciler> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="RequestReconciler"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="libraryMatcher">The library matcher.</param>
  /// <param name="notificationService">The notification service.</param>
  /// <param name="dispatcher">The download dispatcher (to nudge the backend to import a manual file).</param>
  /// <param name="logger">The logger.</param>
  public RequestReconciler(IRequestStore store, ILibraryMatcher libraryMatcher, INotificationService notificationService, IDownloadDispatcher dispatcher, ILogger<RequestReconciler> logger)
  {
    _store = store;
    _libraryMatcher = libraryMatcher;
    _notificationService = notificationService;
    _dispatcher = dispatcher;
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

      // Grant ownership to EVERY active requester once the media is present: not just Approved
      // requests but also still-Pending ones (e.g. several people requested the same unreleased title
      // — each must own it on release, without waiting for individual approval, since it's already
      // in the library and shared ownership adds no new download).
      if (request.Status is RequestStatus.Approved or RequestStatus.Pending)
      {
        var itemId = ResolveItemId(request);
        if (itemId is not null)
        {
          await _store.MarkAvailableAsync(request.Id, itemId, cancellationToken).ConfigureAwait(false);
          await _notificationService.NotifyRequestEventAsync(request, NotificationEvent.Available, cancellationToken).ConfigureAwait(false);

          // The media is present in Jellyfin but may have been placed manually (the backend failed to grab
          // it, or the user side-loaded it). If this request went through a backend, nudge it to rescan the
          // folder from disk so it imports the file and stops searching. Best-effort.
          if (request.DispatchedAt is not null)
          {
            await _dispatcher.RescanAsync(request, cancellationToken).ConfigureAwait(false);
          }

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
