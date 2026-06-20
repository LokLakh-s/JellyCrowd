using System;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure resolution of per-user request permissions from the plugin configuration: whether a user may
/// request, their effective request rate limit, and whether a request should skip the admin queue.
/// </summary>
public static class RequestPolicy
{
  /// <summary>
  /// Finds the user's policy override, or <c>null</c> when none is configured.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="userId">The user id.</param>
  /// <returns>The override, or <c>null</c>.</returns>
  public static UserQuotaOverride? Find(PluginConfiguration config, Guid userId)
  {
    ArgumentNullException.ThrowIfNull(config);
    foreach (var over in config.QuotaOverrides)
    {
      if (over.UserId == userId)
      {
        return over;
      }
    }

    return null;
  }

  /// <summary>
  /// Whether the user may create requests (default: yes).
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="userId">The user id.</param>
  /// <returns><c>true</c> when requests are allowed.</returns>
  public static bool CanRequest(PluginConfiguration config, Guid userId)
    => Find(config, userId)?.CanRequest ?? true;

  /// <summary>
  /// The user's effective request rate limit per period (per-user override, else the global value).
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="userId">The user id.</param>
  /// <returns>The max requests per period (0 = unlimited).</returns>
  public static int MaxRequestsPerPeriod(PluginConfiguration config, Guid userId)
    => Find(config, userId)?.MaxRequestsPerPeriod ?? config.MaxRequestsPerPeriod;

  /// <summary>
  /// Whether this user is trusted (their requests are auto-approved).
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="userId">The user id.</param>
  /// <returns><c>true</c> when the user is trusted.</returns>
  public static bool IsTrusted(PluginConfiguration config, Guid userId)
    => Find(config, userId)?.AutoApprove ?? false;

  /// <summary>
  /// Whether a request should be auto-approved (skip the admin queue): the user is trusted, or the
  /// global size rule applies to the request's estimated size.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="userId">The user id.</param>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <returns><c>true</c> when the request should be auto-approved.</returns>
  public static bool ShouldAutoApprove(PluginConfiguration config, Guid userId, string mediaType)
  {
    if (IsTrusted(config, userId))
    {
      return true;
    }

    var threshold = config.AutoApproveMaxSizeBytes;
    if (threshold <= 0)
    {
      return false;
    }

    var estimate = string.Equals(mediaType, "tv", StringComparison.Ordinal)
      ? config.EstimatedEpisodeSizeBytes
      : config.EstimatedMovieSizeBytes;
    return estimate <= threshold;
  }
}
