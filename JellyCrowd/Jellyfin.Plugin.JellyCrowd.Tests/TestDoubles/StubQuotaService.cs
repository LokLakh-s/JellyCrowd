using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;

namespace Jellyfin.Plugin.JellyCrowd.Tests;

/// <summary>
/// A minimal <see cref="IQuotaService"/> for tests: reports a fixed within/over-quota answer.
/// </summary>
internal sealed class StubQuotaService : IQuotaService
{
  private readonly bool _within;

  public StubQuotaService(bool within) => _within = within;

  public long GetQuotaBytes(Guid userId) => 0;

  public long GetBaseQuotaBytes(Guid userId) => 0;

  public Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(new QuotaInfo());

  public Task<IReadOnlyDictionary<Guid, QuotaInfo>> GetUsageAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyDictionary<Guid, QuotaInfo>>(userIds.ToDictionary(id => id, _ => new QuotaInfo()));

  public Task<bool> CanRequestAsync(Guid userId, string mediaType, int episodes, CancellationToken cancellationToken) => Task.FromResult(_within);

  public Task<bool> IsWithinQuotaAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(_within);

  public long ReservationBytes(RequestRecord request) => 0;

  public Task<long> GetCommittedBytesAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(0L);

}
