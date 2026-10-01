using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Applies parental controls to the catalog and to requests: a user limited by Jellyfin (maximum parental
/// rating, blocked unrated items) or placed in a child group neither sees nor requests a title above
/// their limit. Titles are judged on their TMDB rating, scored by Jellyfin's own rating system.
/// </summary>
public interface IContentRestrictionService
{
  /// <summary>
  /// Resolves the restriction that applies to a user.
  /// </summary>
  /// <param name="userId">The user id.</param>
  /// <returns>The user's restriction; <see cref="ContentRestriction.None"/>-like when nothing is limited.</returns>
  ContentRestriction For(Guid userId);

  /// <summary>
  /// Whether a title is allowed under a restriction. Throws when its rating cannot be looked up, so a
  /// caller deciding on a request can refuse to guess.
  /// </summary>
  /// <param name="restriction">The user's restriction.</param>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The title's TMDB id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns><c>true</c> when the title is allowed.</returns>
  Task<bool> IsAllowedAsync(ContentRestriction restriction, string mediaType, int tmdbId, CancellationToken cancellationToken);

  /// <summary>
  /// Keeps the titles allowed under a restriction, in their original order. A title whose rating cannot be
  /// looked up is left out: when in doubt, a restricted user is shown less, not more.
  /// </summary>
  /// <param name="restriction">The user's restriction.</param>
  /// <param name="items">The catalog items.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The allowed items (the same list when nothing is restricted).</returns>
  Task<IReadOnlyList<CatalogItem>> FilterAsync(ContentRestriction restriction, IReadOnlyList<CatalogItem> items, CancellationToken cancellationToken);
}
