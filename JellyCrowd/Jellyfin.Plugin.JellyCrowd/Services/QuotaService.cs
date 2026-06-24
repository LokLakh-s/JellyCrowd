using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IQuotaService"/>. Usage is the on-disk size of the user's fulfilled
/// (<see cref="RequestStatus.Available"/>) requests; in-flight requests count via configured estimates.
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
    foreach (var over in config.QuotaOverrides)
    {
      if (over.UserId == userId)
      {
        return over.QuotaBytes ?? config.DefaultUserQuotaBytes;
      }
    }

    return config.DefaultUserQuotaBytes;
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
  public async Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken)
  {
    var config = _configurationProvider();
    var quota = GetQuotaBytes(userId);
    var requests = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);

    // Usage is provisional: fulfilled requests count their real on-disk size, while in-flight
    // (Pending/Approved) requests count a per-type estimate so a freshly-made request immediately
    // shows against the quota (the figure self-corrects to the real size once the media lands).
    long used = 0;
    var counted = new HashSet<string>(StringComparer.Ordinal);
    foreach (var request in requests)
    {
      if (request.Status == RequestStatus.Available)
      {
        if (counted.Add(TitleKey(request)))
        {
          used += _libraryMatcher.GetSizeBytes(request.MediaType, request.TmdbId);
        }
      }
      else if (request.Status is RequestStatus.Pending or RequestStatus.Approved)
      {
        used += EstimateBytes(request.MediaType);
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

    var requests = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);

    long committed = 0;
    var counted = new HashSet<string>(StringComparer.Ordinal);
    foreach (var request in requests)
    {
      if (request.Status == RequestStatus.Available)
      {
        if (counted.Add(TitleKey(request)))
        {
          committed += _libraryMatcher.GetSizeBytes(request.MediaType, request.TmdbId);
        }
      }
      else if (request.Status is RequestStatus.Pending or RequestStatus.Approved)
      {
        committed += EstimateBytes(request.MediaType);
      }
    }

    return committed + EstimateBytes(mediaType) <= quota;
  }

  private static string TitleKey(RequestRecord request)
    => request.MediaType + ":" + request.TmdbId.ToString(CultureInfo.InvariantCulture);

  private long EstimateBytes(string mediaType)
  {
    var config = _configurationProvider();
    return string.Equals(mediaType, "tv", StringComparison.Ordinal)
      ? config.EstimatedEpisodeSizeBytes
      : config.EstimatedMovieSizeBytes;
  }
}
