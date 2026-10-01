namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// One episode as Sonarr lists it.
/// </summary>
/// <param name="Id">The Sonarr episode id.</param>
/// <param name="Season">The season number.</param>
/// <param name="Number">The episode number within the season.</param>
/// <param name="Monitored">Whether Sonarr monitors it.</param>
/// <param name="FileId">The id of its file, or 0 when it has none.</param>
public readonly record struct SonarrEpisode(int Id, int Season, int Number, bool Monitored, int FileId);
