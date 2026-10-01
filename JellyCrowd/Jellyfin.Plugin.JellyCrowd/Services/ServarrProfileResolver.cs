using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IServarrProfileResolver"/>: while language preferences are offered, every user who
/// asked for the title (any season, any status but denied) weighs in through their preference; they agree
/// on one profile, or the title takes the common one. A show has one profile in Sonarr, so all its seasons
/// count together.
/// </summary>
public sealed class ServarrProfileResolver : IServarrProfileResolver
{
  private readonly IRequestStore _requests;
  private readonly IUserPrefsStore _prefs;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="ServarrProfileResolver"/> class.
  /// </summary>
  /// <param name="requests">The request store (who asked for the title).</param>
  /// <param name="prefs">The user preferences (their preferred version).</param>
  /// <param name="config">The plugin configuration accessor (the profiles).</param>
  public ServarrProfileResolver(IRequestStore requests, IUserPrefsStore prefs, Func<PluginConfiguration> config)
  {
    _requests = requests;
    _prefs = prefs;
    _config = config;
  }

  /// <inheritdoc />
  public async Task<int?> ResolveAsync(string mediaType, int tmdbId, CancellationToken cancellationToken)
  {
    var config = _config();
    var isMovie = string.Equals(mediaType, "movie", StringComparison.Ordinal);
    var common = isMovie ? config.RadarrQualityProfileId : config.SonarrQualityProfileId;
    if (!config.LanguagePreferencesEnabled)
    {
      return null;
    }

    var requesters = (await _requests.GetAllAsync(cancellationToken).ConfigureAwait(false))
      .Where(r => r.TmdbId == tmdbId && string.Equals(r.MediaType, mediaType, StringComparison.Ordinal) && r.Status != RequestStatus.Denied)
      .Select(r => r.UserId)
      .Distinct()
      .ToList();

    var preferences = new List<string>();
    foreach (var userId in requesters)
    {
      preferences.Add((await _prefs.GetAsync(userId, cancellationToken).ConfigureAwait(false)).LanguagePreference);
    }

    return LanguagePolicy.TargetProfile(
      preferences,
      p => isMovie
        ? LanguagePolicy.ProfileFor(p, config.RadarrProfileOriginal, config.RadarrProfileDubbed, config.RadarrProfileSubtitled, common)
        : LanguagePolicy.ProfileFor(p, config.SonarrProfileOriginal, config.SonarrProfileDubbed, config.SonarrProfileSubtitled, common),
      common);
  }
}
