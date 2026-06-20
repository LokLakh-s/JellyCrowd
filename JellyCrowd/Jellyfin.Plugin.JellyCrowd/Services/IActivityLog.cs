using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// The plugin's internal activity log: records lifecycle/admin/download events for the admin Logs
/// tab. Bounded (most-recent cap + retention) so it never grows without limit.
/// </summary>
public interface IActivityLog
{
  /// <summary>
  /// Records an activity entry (best-effort; safe to fire-and-forget).
  /// </summary>
  /// <param name="level">The severity (<c>info</c>, <c>warning</c>, <c>error</c>).</param>
  /// <param name="category">The category (<c>request</c>, <c>download</c>, <c>admin</c>, …).</param>
  /// <param name="message">The message.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the entry is stored.</returns>
  Task LogAsync(string level, string category, string message, CancellationToken cancellationToken);

  /// <summary>
  /// Queries the activity log, newest first, optionally filtered by term/category/level.
  /// </summary>
  /// <param name="term">Free-text filter on the message (case-insensitive), or <c>null</c>.</param>
  /// <param name="category">Category filter, or <c>null</c> for all.</param>
  /// <param name="level">Level filter, or <c>null</c> for all.</param>
  /// <param name="limit">Maximum number of entries to return.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The matching entries.</returns>
  Task<IReadOnlyList<ActivityEntry>> QueryAsync(string? term, string? category, string? level, int limit, CancellationToken cancellationToken);
}
