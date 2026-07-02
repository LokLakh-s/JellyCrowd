using System;
using System.Collections.Generic;
using System.Linq;
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
  /// Whether the plugin should be visible/usable for a user. Administrators always see it. A per-user
  /// override (<see cref="UserQuotaOverride.PluginAccess"/>) wins over the global "config mode" in both
  /// directions: <c>true</c> forces access even while config mode hides the plugin; <c>false</c> blocks
  /// this user even when the plugin is otherwise visible. With no override (<c>null</c>) the user follows
  /// config mode — hidden when it is on, visible when it is off.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="userId">The user id.</param>
  /// <param name="isAdmin">Whether the user is an administrator.</param>
  /// <returns><c>true</c> when the plugin is visible to the user.</returns>
  public static bool IsVisibleTo(PluginConfiguration config, Guid userId, bool isAdmin)
  {
    ArgumentNullException.ThrowIfNull(config);
    if (isAdmin)
    {
      return true;
    }

    var access = Find(config, userId)?.PluginAccess;
    if (access.HasValue)
    {
      return access.Value;
    }

    return !config.HiddenFromUsers;
  }

  /// <summary>
  /// Whether a request should be auto-approved (skip the admin queue): the user is trusted, or the
  /// global size rule applies to the request's estimated size — further gated, when configured, by the
  /// title's genres matching <see cref="PluginConfiguration.AutoApproveGenres"/>.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="userId">The user id.</param>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="genres">The title's genre names (used only when an auto-approve genre list is set).</param>
  /// <returns><c>true</c> when the request should be auto-approved.</returns>
  public static bool ShouldAutoApprove(PluginConfiguration config, Guid userId, string mediaType, IReadOnlyList<string>? genres = null)
  {
    ArgumentNullException.ThrowIfNull(config);
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
    if (estimate > threshold)
    {
      return false;
    }

    // Optional genre gate: when a list is configured, require at least one matching genre.
    if (config.AutoApproveGenres.Count == 0)
    {
      return true;
    }

    if (genres is null || genres.Count == 0)
    {
      return false;
    }

    return genres.Any(g => config.AutoApproveGenres.Any(a => string.Equals(a, g, StringComparison.OrdinalIgnoreCase)));
  }
}
