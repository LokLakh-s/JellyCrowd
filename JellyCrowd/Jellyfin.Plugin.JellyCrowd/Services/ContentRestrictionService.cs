using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IContentRestrictionService"/>: reads the user's Jellyfin policy and child group,
/// looks titles' ratings up on TMDB (cached by <see cref="CachingTmdbClient"/>) and scores them with
/// Jellyfin's localization manager, in the server's metadata country.
/// </summary>
public sealed class ContentRestrictionService : IContentRestrictionService
{
  // Rating lookups for one page of results run in parallel, but not all at once: TMDB rate-limits.
  private const int MaxConcurrentLookups = 4;

  private readonly Func<PluginConfiguration> _config;
  private readonly IUserManager _userManager;
  private readonly ILocalizationManager _localization;
  private readonly IServerConfigurationManager _serverConfig;
  private readonly ITmdbClient _tmdbClient;
  private readonly ILogger<ContentRestrictionService> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="ContentRestrictionService"/> class.
  /// </summary>
  /// <param name="config">The plugin configuration accessor (child groups).</param>
  /// <param name="userManager">The user manager (parental control policy).</param>
  /// <param name="localization">The localization manager (rating scores).</param>
  /// <param name="serverConfig">The server configuration (metadata country).</param>
  /// <param name="tmdbClient">The TMDB client (title ratings).</param>
  /// <param name="logger">The logger.</param>
  public ContentRestrictionService(
    Func<PluginConfiguration> config,
    IUserManager userManager,
    ILocalizationManager localization,
    IServerConfigurationManager serverConfig,
    ITmdbClient tmdbClient,
    ILogger<ContentRestrictionService> logger)
  {
    _config = config;
    _userManager = userManager;
    _localization = localization;
    _serverConfig = serverConfig;
    _tmdbClient = tmdbClient;
    _logger = logger;
  }

  /// <inheritdoc />
  public ContentRestriction For(Guid userId)
  {
    var policy = ReadPolicy(userId);
    var blocked = policy?.BlockUnratedItems ?? Array.Empty<UnratedItem>();

    // An administrator is never a child account, whatever group they sit in — the same rule the client
    // follows. Jellyfin's own parental control applies to everyone, as it does in the library.
    var child = RequestPolicy.ChildPolicyFor(_config(), userId);
    var childAge = child.IsChild && policy?.IsAdministrator != true ? child.MaxAge : (int?)null;

    return ContentRestrictionPolicy.Combine(
      policy?.MaxParentalRating,
      policy?.MaxParentalSubRating,
      blocked.Contains(UnratedItem.Movie),
      blocked.Contains(UnratedItem.Series),
      childAge);
  }

  /// <inheritdoc />
  public async Task<bool> IsAllowedAsync(ContentRestriction restriction, string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(restriction);
    if (!restriction.IsRestricted)
    {
      return true;
    }

    var isMovie = string.Equals(mediaType, "movie", StringComparison.Ordinal);
    var byCountry = await _tmdbClient.GetCertificationsAsync(isMovie ? "movie" : "tv", tmdbId, cancellationToken).ConfigureAwait(false);
    var (country, ratings) = ContentRestrictionPolicy.RatingsFor(byCountry, _serverConfig.Configuration.MetadataCountryCode);
    var strictest = ContentRestrictionPolicy.Strictest(ratings
      .Select(r => _localization.GetRatingScore(r, country))
      .Where(s => s is not null)
      .Select(s => (s!.Score, s.SubScore)));
    return ContentRestrictionPolicy.IsAllowed(restriction, strictest?.Score, strictest?.SubScore, isMovie);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<CatalogItem>> FilterAsync(ContentRestriction restriction, IReadOnlyList<CatalogItem> items, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(restriction);
    ArgumentNullException.ThrowIfNull(items);
    if (!restriction.IsRestricted || items.Count == 0)
    {
      return items;
    }

    using var gate = new SemaphoreSlim(MaxConcurrentLookups);
    var verdicts = await Task.WhenAll(items.Select(async item =>
    {
      await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        return await IsAllowedAsync(restriction, item.MediaType, item.TmdbId, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
      {
        // Unknown rating (TMDB unreachable or timed out): leave the title out rather than risk it.
        _logger.LogDebug(ex, "Could not look up the rating of {MediaType} {TmdbId}; hidden from a restricted user", item.MediaType, item.TmdbId);
        return false;
      }
      finally
      {
        gate.Release();
      }
    })).ConfigureAwait(false);

    return items.Where((_, index) => verdicts[index]).ToList();
  }

  // The Jellyfin policy of a user, or null when the user cannot be read (unknown user, anonymous caller).
  private UserPolicy? ReadPolicy(Guid userId)
  {
    if (userId == Guid.Empty)
    {
      return null;
    }

    try
    {
      var user = _userManager.GetUserById(userId);
      return user is null ? null : _userManager.GetUserDto(user, string.Empty)?.Policy;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Could not read the parental control of user {UserId}", userId);
      return null;
    }
  }
}
