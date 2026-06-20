using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Stores user-submitted issue reports for admin triage. Bounded so the store stays small.
/// </summary>
public interface IReportStore
{
  /// <summary>
  /// Adds a report (assigning id/timestamp) and trims the store to the global cap.
  /// </summary>
  /// <param name="report">The report to add.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The stored report.</returns>
  Task<MediaReport> AddAsync(MediaReport report, CancellationToken cancellationToken);

  /// <summary>
  /// Gets all reports, newest first.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The reports.</returns>
  Task<IReadOnlyList<MediaReport>> GetAllAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Sets (or clears) the resolved flag on a report.
  /// </summary>
  /// <param name="id">The report id.</param>
  /// <param name="resolved">Whether the report is resolved.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The updated report, or <c>null</c> if not found.</returns>
  Task<MediaReport?> SetResolvedAsync(Guid id, bool resolved, CancellationToken cancellationToken);

  /// <summary>
  /// Deletes a report.
  /// </summary>
  /// <param name="id">The report id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when a report was removed.</returns>
  Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
