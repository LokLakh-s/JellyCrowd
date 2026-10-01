using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;

namespace Jellyfin.Plugin.JellyCrowd.Tests;

/// <summary>
/// A configurable <see cref="IContentRestrictionService"/>: unrestricted by default; once given a
/// <see cref="Restriction"/>, only the titles listed in <see cref="Allowed"/> pass.
/// </summary>
internal sealed class FakeContentRestrictionService : IContentRestrictionService
{
  /// <summary>Gets or sets the restriction returned for every user.</summary>
  public ContentRestriction Restriction { get; set; } = ContentRestriction.None;

  /// <summary>Gets the titles allowed under a restriction, as <c>"{mediaType}:{tmdbId}"</c>.</summary>
  public HashSet<string> Allowed { get; } = new(StringComparer.Ordinal);

  /// <summary>Gets or sets a value indicating whether rating lookups fail, as when TMDB is unreachable.</summary>
  public bool Unavailable { get; set; }

  public ContentRestriction For(Guid userId) => Restriction;

  public Task<bool> IsAllowedAsync(ContentRestriction restriction, string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    if (!restriction.IsRestricted)
    {
      return Task.FromResult(true);
    }

    if (Unavailable)
    {
      throw new HttpRequestException("TMDB is unreachable.");
    }

    return Task.FromResult(Allowed.Contains(mediaType + ":" + tmdbId));
  }

  public Task<IReadOnlyList<CatalogItem>> FilterAsync(ContentRestriction restriction, IReadOnlyList<CatalogItem> items, CancellationToken cancellationToken)
    => Task.FromResult<IReadOnlyList<CatalogItem>>(restriction.IsRestricted
      ? items.Where(i => Allowed.Contains(i.MediaType + ":" + i.TmdbId)).ToList()
      : items);
}
