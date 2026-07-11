using System;
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

  public Task<bool> CanRequestAsync(Guid userId, string mediaType, CancellationToken cancellationToken) => Task.FromResult(_within);

  public Task<bool> IsWithinQuotaAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(_within);
}
