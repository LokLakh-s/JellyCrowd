using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Computes per-user disk usage and enforces quotas.
/// </summary>
public interface IQuotaService
{
  /// <summary>
  /// Gets the effective quota (in bytes) for a user: the base quota adjusted by the adaptive tier when the
  /// adaptive quota is enabled, otherwise the base quota itself. 0 means unlimited.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <returns>The quota in bytes.</returns>
  long GetQuotaBytes(Guid userId);

  /// <summary>
  /// Gets the base quota (in bytes) for a user before any adaptive adjustment: their override if any,
  /// otherwise the global default. 0 means unlimited.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <returns>The base quota in bytes.</returns>
  long GetBaseQuotaBytes(Guid userId);

  /// <summary>
  /// Gets the current usage snapshot for a user (actual bytes used by fulfilled requests vs quota).
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The usage snapshot.</returns>
  Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Gets the usage of several users in one pass, sharing a single size lookup across them. Usage is
  /// summed by querying the library for the size of each owned title — and in a shared library the same
  /// title is owned by many people, so computing users one by one re-asks for sizes that cannot differ.
  /// </summary>
  /// <param name="userIds">The users to measure.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>Each user's usage, keyed by user id.</returns>
  Task<IReadOnlyDictionary<Guid, QuotaInfo>> GetUsageAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);

  /// <summary>
  /// Determines whether the user can make a new request of the given media type without exceeding their quota.
  /// Considers actual used bytes plus estimates for in-flight requests and the new one.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="mediaType">The media type being requested (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="episodes">How many episodes the request covers — 1 for a movie or a single episode, the
  /// whole count for a season or series request, which is what it will actually download.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the request fits within the quota.</returns>
  Task<bool> CanRequestAsync(Guid userId, string mediaType, int episodes, CancellationToken cancellationToken);

  /// <summary>
  /// The quota a single in-flight request reserves: the configured estimate for its media type, times the
  /// episodes it covers.
  /// </summary>
  /// <param name="request">The request.</param>
  /// <returns>The reserved bytes.</returns>
  long ReservationBytes(RequestRecord request);

  /// <summary>
  /// The user's committed footprint: fulfilled requests at their real size on disk plus the reservations
  /// of the requests actually in flight. Requests held for quota are excluded — they are not downloading,
  /// so they must not count against the decision to release them.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The committed bytes.</returns>
  Task<long> GetCommittedBytesAsync(Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Whether the user's library has grown past their quota: what they own on disk, strictly above the
  /// limit. Being exactly at the limit is not over it. This is the figure their gauge shows, and it
  /// deliberately leaves out the reservations of requests in flight — those are pessimistic upper bounds
  /// only administrators see, so they must never be what costs someone an ownership. Contrast
  /// <see cref="IsWithinQuotaAsync"/>, which weighs the whole committed footprint to decide whether a
  /// request held for quota may resume.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the user owns more than their quota (never for an unlimited quota).</returns>
  Task<bool> IsOverQuotaAsync(Guid userId, CancellationToken cancellationToken);

  /// <summary>
  /// Determines whether the user's current committed footprint (their existing in-flight estimates plus
  /// the real size of fulfilled requests) is within their quota — i.e. nothing new is added. Used to
  /// decide whether requests held back purely by the quota can now resume.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the existing footprint fits within the quota (or the quota is unlimited).</returns>
  Task<bool> IsWithinQuotaAsync(Guid userId, CancellationToken cancellationToken);
}
