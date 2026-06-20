using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// A no-op <see cref="IActivityLog"/> for tests that don't assert on the activity log.
/// </summary>
internal sealed class NoOpActivityLog : IActivityLog
{
  public Task LogAsync(string level, string category, string message, CancellationToken cancellationToken)
    => Task.CompletedTask;

  public Task<IReadOnlyList<ActivityEntry>> QueryAsync(string? term, string? category, string? level, int limit, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<ActivityEntry>>(new List<ActivityEntry>());
}
