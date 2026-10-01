using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// The Radarr/Sonarr quality profile a title is fetched with.
/// </summary>
public interface IServarrProfileResolver
{
  /// <summary>
  /// Resolves the profile of a title from its requesters' language preferences.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The title's TMDB id.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The quality profile id, or <c>null</c> when preferences have no say (feature off, or no
  /// requester has one) — the title then keeps the profile it has, and a new one gets the configured profile.</returns>
  Task<int?> ResolveAsync(string mediaType, int tmdbId, CancellationToken cancellationToken);
}
