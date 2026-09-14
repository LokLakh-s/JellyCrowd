namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// One episode of a show, identified by its season and episode numbers.
/// </summary>
/// <param name="Season">The season number.</param>
/// <param name="Episode">The episode number within the season.</param>
public readonly record struct EpisodeKey(int Season, int Episode);
