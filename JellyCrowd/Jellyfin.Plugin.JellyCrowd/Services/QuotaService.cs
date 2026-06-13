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
  private readonly Func<PluginConfiguration> _configurationProvider;

  /// <summary>
  /// Initializes a new instance of the <see cref="QuotaService"/> class.
  /// </summary>
  /// <param name="store">The request store.</param>
  /// <param name="libraryMatcher">The library matcher (for actual sizes).</param>
  /// <param name="configurationProvider">Provides the current plugin configuration.</param>
  public QuotaService(IRequestStore store, ILibraryMatcher libraryMatcher, Func<PluginConfiguration> configurationProvider)
  {
    _store = store;
    _libraryMatcher = libraryMatcher;
    _configurationProvider = configurationProvider;
  }

  /// <inheritdoc />
  public long GetQuotaBytes(Guid userId)
  {
    var config = _configurationProvider();
    foreach (var over in config.QuotaOverrides)
    {
      if (over.UserId == userId)
      {
        return over.QuotaBytes;
      }
    }

    return config.DefaultUserQuotaBytes;
  }

  /// <inheritdoc />
  public async Task<QuotaInfo> GetUsageAsync(Guid userId, CancellationToken cancellationToken)
  {
    var quota = GetQuotaBytes(userId);
    var requests = await _store.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);

    long used = 0;
    var counted = new HashSet<string>(StringComparer.Ordinal);
    foreach (var request in requests)
    {
      if (request.Status == RequestStatus.Available && counted.Add(TitleKey(request)))
      {
        used += _libraryMatcher.GetSizeBytes(request.MediaType, request.TmdbId);
      }
    }

    return new QuotaInfo { UsedBytes = used, QuotaBytes = quota, Unlimited = quota <= 0 };
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
