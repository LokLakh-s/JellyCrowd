using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IQuotaService"/>. Displayed usage is the on-disk size of the user's fulfilled
/// (<see cref="RequestStatus.Available"/>) requests only; in-flight requests count via configured
/// estimates solely inside <see cref="CanRequestAsync"/> to gate (and hold) over-quota requests.
/// </summary>
public sealed class QuotaService : IQuotaService
{
  private readonly IRequestStore _store;
  private readonly ILibraryMatcher _libraryMatcher;
  private readonly IUserActivityStore _activityStore;
  private readonly Func<PluginConfiguration> _configurationProvider;

  /// <summary>
  /// Initializes a new instance of the <see cref="QuotaService"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="libraryMatcher">The library matcher (for actual sizes).</param>
  /// <param name="activityStore">The viewing-activity store (drives the adaptive quota).</param>
  /// <param name="configurationProvider">Provides the current plugin configuration.</param>
  public QuotaService(IRequestStore store, ILibraryMatcher libraryMatcher, IUserActivityStore activityStore, Func<PluginConfiguration> configurationProvider)
  {
    _store = store;
    _libraryMatcher = libraryMatcher;
    _activityStore = activityStore;
    _configurationProvider = configurationProvider;
  }

  /// <inheritdoc />
  public long GetBaseQuotaBytes(Guid userId)
  {
    var config = _configurationProvider();

    // Precedence: a per-user override wins over the user's group, which wins over the global default.
    // With no group configured this is identical to the previous per-user-only resolution.
    var perUser = RequestPolicy.Find(config, userId)?.QuotaBytes;
    var group = RequestPolicy.GroupOf(config, userId)?.QuotaBytes;
    return perUser ?? group ?? config.DefaultUserQuotaBytes;
  }

  /// <inheritdoc />
  public long GetQuotaBytes(Guid userId)
  {
    var config = _configurationProvider();
    var baseBytes = GetBaseQuotaBytes(userId);
    if (!config.AdaptiveQuotaEnabled || baseBytes <= 0)
    {
      return baseBytes;
    }

    return AdaptiveQuotaCalculator.ComputeBytes(config, baseBytes, _activityStore.Get(userId));
  }

  /// <inheritdoc />
  public Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken)
    => GetUsageAsync(userId, new Dictionary<string, long>(StringComparer.Ordinal), cancellationToken);

  /// <inheritdoc />
  public async Task<IReadOnlyDictionary<Guid, QuotaInfo>> GetUsageAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(userIds);

    // One memo for the whole sweep: a title owned by several users is looked up once, not once per owner.
    // Its size on disk is a property of the file, not of who owns it.
    var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
    var result = new Dictionary<Guid, QuotaInfo>(userIds.Count);
    foreach (var userId in userIds)
    {
      result[userId] = await GetUsageAsync(userId, sizes, cancellationToken).ConfigureAwait(false);
    }

    return result;
  }

  private async Task<QuotaInfo> GetUsageAsync(Guid userId, Dictionary<string, long> sizes, CancellationToken cancellationToken)
  {
    var config = _configurationProvider();
    var quota = GetQuotaBytes(userId);
    var requests = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);

    // Displayed usage counts only fulfilled (Available) requests at their real on-disk size. In-flight
    // requests do not count against the displayed figure; their theoretical footprint lives only in
    // CanRequestAsync, where it gates whether a new request would exceed the quota (held pending if so).
    long used = 0;
    var counted = new HashSet<string>(StringComparer.Ordinal);
    foreach (var request in requests)
    {
      if (request.Status == RequestStatus.Available && counted.Add(TitleKey(request)))
      {
        used += SizeOf(request, sizes);
      }
    }

    var info = new QuotaInfo { UsedBytes = used, QuotaBytes = quota, Unlimited = quota <= 0 };
    if (config.AdaptiveQuotaEnabled && GetBaseQuotaBytes(userId) > 0)
    {
      var activity = _activityStore.Get(userId);
      info.AdaptiveEnabled = true;
      info.Tier = activity.Tier switch
      {
        Models.AdaptiveTier.Floor => "floor",
        Models.AdaptiveTier.Ceiling => "ceiling",
        _ => "base",
      };
      if (activity.ProbationStartUtc is { } start)
      {
        info.InProbation = true;
        info.ProbationEndsUtc = start.AddDays(Math.Max(1, config.AdaptiveProbationDays));
      }
    }

    return info;
  }

  /// <inheritdoc />
  public async Task<bool> CanRequestAsync(Guid userId, string mediaType, CancellationToken cancellationToken)
  {
    var quota = GetQuotaBytes(userId);
    if (quota <= 0)
    {
      return true;
    }

    var committed = await ComputeCommittedAsync(userId, cancellationToken).ConfigureAwait(false);
    return committed + EstimateBytes(mediaType) <= quota;
  }

  /// <inheritdoc />
  public async Task<bool> IsWithinQuotaAsync(Guid userId, CancellationToken cancellationToken)
  {
    var quota = GetQuotaBytes(userId);
    if (quota <= 0)
    {
      return true;
    }

    var committed = await ComputeCommittedAsync(userId, cancellationToken).ConfigureAwait(false);
    return committed <= quota;
  }

  // The user's committed footprint: in-flight requests (Pending/Approved) at the configured estimate,
  // fulfilled (Available) requests at their real on-disk size, de-duplicated by title for the latter.
  // A not-yet-released request (its dispatch is deferred to the release date, so DesiredAt is in the
  // future) reserves NO quota — it won't occupy disk until it is out. It starts counting only once it is
  // due, at which point the quota is re-checked before it downloads (see the download dispatcher).
  private async Task<long> ComputeCommittedAsync(Guid userId, CancellationToken cancellationToken)
  {
    var requests = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    var now = DateTime.UtcNow;

    long committed = 0;
    var counted = new HashSet<string>(StringComparer.Ordinal);
    foreach (var request in requests)
    {
      if (request.Status == RequestStatus.Available)
      {
        if (counted.Add(TitleKey(request)))
        {
          committed += _libraryMatcher.GetSizeBytes(request.MediaType, request.TmdbId, request.Season, request.Episode);
        }
      }
      else if ((request.Status is RequestStatus.Pending or RequestStatus.Approved)
        && (request.DesiredAt is null || request.DesiredAt <= now))
      {
        committed += EstimateBytes(request.MediaType);
      }
    }

    return committed;
  }

  // The library query behind a size is the expensive part of a quota sweep; the same title never has two
  // sizes, so remember it for the life of the caller's computation (no cross-request cache, no staleness).
  private long SizeOf(RequestRecord request, Dictionary<string, long> sizes)
  {
    var key = TitleKey(request);
    if (!sizes.TryGetValue(key, out var size))
    {
      size = _libraryMatcher.GetSizeBytes(request.MediaType, request.TmdbId, request.Season, request.Episode);
      sizes[key] = size;
    }

    return size;
  }

  private static string TitleKey(RequestRecord request)
    => request.MediaType + ":" + request.TmdbId.ToString(CultureInfo.InvariantCulture)
      + ":" + (request.Season?.ToString(CultureInfo.InvariantCulture) ?? "*")
      + ":" + (request.Episode?.ToString(CultureInfo.InvariantCulture) ?? "*");

  private long EstimateBytes(string mediaType)
  {
    var config = _configurationProvider();
    return string.Equals(mediaType, "tv", StringComparison.Ordinal)
      ? config.EstimatedEpisodeSizeBytes
      : config.EstimatedMovieSizeBytes;
  }
}
